using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Catan.Client.Animation;
using Catan.Core;

namespace Catan.Client
{
    public sealed partial class MainWindow
    {
        /// <summary>Shows whichever dialog the current state calls for (or hides the overlay).</summary>
        void BuildOverlay()
        {
            _overlay.Children.Clear();
            Game g = _c.Game;

            Control content = null;
            if (g == null || _modal == Modal.Setup) content = BuildSetup(g != null);
            else if (_c.HandoffPending) content = BuildHandoff();
            else if (g.Phase == Phase.Discard) content = BuildDiscard();
            else
            {
                switch (_modal)
                {
                    case Modal.BankTrade: content = BuildBankTrade(); break;
                    case Modal.PlayerTrade: content = BuildPlayerTrade(); break;
                    case Modal.PlayCard: content = BuildPlayCard(); break;
                    case Modal.PickYearOfPlenty: content = BuildPickResource("Year of Plenty", _yearFirst == Resource.Desert ? "Pick the first resource" : "Pick the second resource", PickYear); break;
                    case Modal.PickMonopoly: content = BuildPickResource("Monopoly", "Take every card of one resource from the other players", PickMonopoly); break;
                    case Modal.Rules: content = BuildRules(); break;
                }
            }

            _overlay.IsVisible = content != null;
            if (content == null) return;

            // The backdrop swallows clicks so the board underneath can't be used by accident. During a hand-off
            // it is fully opaque so the next player can't read the previous player's hand off the screen.
            bool opaque = g != null && _modal != Modal.Setup && _c.HandoffPending;
            _overlay.Children.Add(new Border
            {
                Background = Palette.Brush(opaque ? Color.FromRgb(0x0c, 0x0e, 0x12) : Color.FromArgb(170, 0, 0, 0)),
            });
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            _overlay.Children.Add(content);
        }

        // ---- New game ------------------------------------------------------------------------------

        Control BuildSetup(bool canGoBack)
        {
            var tiles = Ui.Text($"{Core.Hex.CountForRadius(_setupRadius)} tiles", 12, false, Ui.Muted);

            var col = Ui.Column(10,
                Ui.Text("Catan", 32, true),
                Ui.Text("Hot-seat game: pass the device between players.", 13, false, Ui.Muted),
                new Border { Height = 6 },
                Ui.Stepper("Players", _setupPlayers, 2, 6, v => _setupPlayers = v),
                Ui.Stepper("Board radius", _setupRadius, BoardGenerator.MinRadius, 6, v =>
                {
                    _setupRadius = v;
                    tiles.Text = $"{Core.Hex.CountForRadius(v)} tiles";
                }),
                tiles,
                Ui.Stepper("Points to win", _setupVp, 3, 20, v => _setupVp = v));

            var hide = new CheckBox { Content = "Hide hands between turns", IsChecked = _setupHide, Foreground = Palette.Brush(Colors.White) };
            hide.IsCheckedChanged += (_, _) => _setupHide = hide.IsChecked == true;
            col.Children.Add(hide);
            var anim = new CheckBox { Content = "Animations", IsChecked = AnimationLayer.Enabled, Foreground = Palette.Brush(Colors.White) };
            anim.IsCheckedChanged += (_, _) => AnimationLayer.Enabled = anim.IsChecked == true;
            col.Children.Add(anim);
            col.Children.Add(Ui.Text("More options are under House Rules once the game starts.", 12, false, Ui.Muted));
            col.Children.Add(new Border { Height = 6 });

            var buttons = Ui.Row(8, Ui.Button("Start game", StartGame, primary: true, minWidth: 140));
            if (canGoBack) buttons.Children.Add(Ui.Button("Back to game", CloseModal));
            col.Children.Add(buttons);
            return Ui.Card(col, 460);
        }

        void StartGame()
        {
            _modal = Modal.None;
            _c.NewGame(_setupPlayers, _setupRadius, _setupVp, _setupHide);
        }

        // ---- Pass the device -----------------------------------------------------------------------

        Control BuildHandoff()
        {
            Player next = _c.Game.Players[_c.Actor];
            var col = Ui.Column(14,
                Ui.Text("Pass the device to", 18, false, Ui.Muted),
                Ui.Row(12, Ui.Dot(Palette.Player(next.Id), 28), Ui.Text(next.Name, 34, true)),
                Ui.Button("Ready", _c.AcknowledgeHandoff, primary: true, minWidth: 160));
            col.HorizontalAlignment = HorizontalAlignment.Center;
            return Ui.Card(col, 460);
        }

        // ---- Discard -------------------------------------------------------------------------------

        Control BuildDiscard()
        {
            Game g = _c.Game;
            int who = _c.Actor;
            if (who != _discardFor)
            {
                Array.Clear(_discardSel, 0, 5);
                _discardFor = who;
            }

            Player p = g.Players[who];
            int owe = g.PendingDiscards[who];
            var col = Ui.Column(8,
                Ui.Row(8, Ui.Dot(Palette.Player(who), 16), Ui.Text($"{p.Name}: discard {owe} cards", 20, true)),
                Ui.Text("A 7 was rolled and you hold too many cards.", 13, false, Ui.Muted));

            var selected = Ui.Text($"Selected {_discardSel.Sum()} of {owe}", 14, true);
            var discardBtn = Ui.Button("Discard", () =>
            {
                if (_c.Send(new DiscardCards(who, ToSet(_discardSel))))
                {
                    Array.Clear(_discardSel, 0, 5);
                    _discardFor = -1;
                }
            }, _discardSel.Sum() == owe, primary: true, minWidth: 140);

            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                Resource r = ResourceSet.Types[i];
                col.Children.Add(Ui.Stepper($"{r} (have {p.Hand[r]})", _discardSel[i], 0, p.Hand[r], v =>
                {
                    _discardSel[idx] = v;
                    selected.Text = $"Selected {_discardSel.Sum()} of {owe}";
                    discardBtn.IsEnabled = _discardSel.Sum() == owe;
                }));
            }
            col.Children.Add(selected);
            col.Children.Add(discardBtn);
            return Ui.Card(col, 460);
        }

        static ResourceSet ToSet(int[] a) => new ResourceSet(a[0], a[1], a[2], a[3], a[4]);

        // ---- Bank trade ----------------------------------------------------------------------------

        Control BuildBankTrade()
        {
            Game g = _c.Game;
            int me = g.CurrentPlayer;
            var col = Ui.Column(10, Ui.Text("Bank trade", 22, true), Ui.Text("Give:", 14, true));

            var give = new WrapPanel();
            foreach (Resource r in ResourceSet.Types)
            {
                Resource res = r;
                int ratio = g.GetBankRatio(me, res);
                Button b = Ui.Choice($"{res}\n{ratio}:1 (have {g.Players[me].Hand[res]})", _bankGive == res,
                    () => { _bankGive = res; if (_bankGet == res) _bankGet = Resource.Desert; BuildOverlay(); },
                    g.Players[me].Hand[res] >= ratio);
                b.Margin = new Thickness(0, 0, 6, 6);
                give.Children.Add(b);
            }
            col.Children.Add(give);

            col.Children.Add(Ui.Text("Get 1:", 14, true));
            var get = new WrapPanel();
            foreach (Resource r in ResourceSet.Types)
            {
                Resource res = r;
                Button b = Ui.Choice(res.ToString(), _bankGet == res, () => { _bankGet = res; BuildOverlay(); }, g.Bank[res] > 0 && res != _bankGive);
                b.Margin = new Thickness(0, 0, 6, 6);
                get.Children.Add(b);
            }
            col.Children.Add(get);

            col.Children.Add(Ui.Row(8,
                Ui.Button("Trade", () =>
                {
                    if (_c.Send(new BankTrade(me, _bankGive, _bankGet))) CloseModal();
                }, _bankGive != Resource.Desert && _bankGet != Resource.Desert, primary: true, minWidth: 120),
                Ui.Button("Close", CloseModal)));
            return Ui.Card(col, 560);
        }

        // ---- Player trade --------------------------------------------------------------------------

        Control BuildPlayerTrade()
        {
            Game g = _c.Game;
            int me = g.CurrentPlayer;
            var col = Ui.Column(8,
                Ui.Text("Offer a trade to the table", 22, true),
                Ui.Text("Any other player can accept it from the bar at the bottom.", 13, false, Ui.Muted));

            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                Resource r = ResourceSet.Types[i];
                var name = Ui.Text($"{r} (have {g.Players[me].Hand[r]})", 14, true, Palette.Resource(r));
                name.Width = 150;
                name.VerticalAlignment = VerticalAlignment.Center;
                col.Children.Add(Ui.Row(10, name,
                    Ui.Stepper("give", _offerGive[i], 0, g.Players[me].Hand[r], v => _offerGive[idx] = v, 50),
                    Ui.Stepper("want", _offerWant[i], 0, 19, v => _offerWant[idx] = v, 50)));
            }

            col.Children.Add(Ui.Row(8,
                Ui.Button("Propose", () =>
                {
                    if (_c.Send(new ProposeTrade(me, ToSet(_offerGive), ToSet(_offerWant)))) CloseModal();
                }, primary: true, minWidth: 120),
                Ui.Button("Close", CloseModal)));
            return Ui.Card(col, 640);
        }

        // ---- Development cards ---------------------------------------------------------------------

        Control BuildPlayCard()
        {
            Game g = _c.Game;
            Player me = g.Players[g.CurrentPlayer];
            var col = Ui.Column(8, Ui.Text("Play a development card", 22, true));
            if (g.DevCardPlayedThisTurn && g.Rules.OneDevCardPerTurn)
                col.Children.Add(Ui.Text("You already played a card this turn.", 13, false, Ui.Muted));

            void Row(DevCard card, Action play)
            {
                col.Children.Add(Ui.Button($"{card}  (x{me.DevCardsUsable(card)})", play, me.DevCardsUsable(card) > 0, minWidth: 260));
            }

            Row(DevCard.Knight, () => { if (_c.Send(new PlayKnight(me.Id))) CloseModal(); });
            Row(DevCard.RoadBuilding, () => { if (_c.Send(new PlayRoadBuilding(me.Id))) CloseModal(); });
            Row(DevCard.YearOfPlenty, () => { _yearFirst = Resource.Desert; OpenModal(Modal.PickYearOfPlenty); });
            Row(DevCard.Monopoly, () => OpenModal(Modal.PickMonopoly));

            col.Children.Add(Ui.Text($"Victory Point cards in hand: {me.DevCardsTotal(DevCard.VictoryPoint)} (they count automatically)", 12, false, Ui.Muted));
            col.Children.Add(Ui.Button("Close", CloseModal));
            return Ui.Card(col, 420);
        }

        Control BuildPickResource(string title, string hint, Action<Resource> onPick)
        {
            var col = Ui.Column(10, Ui.Text(title, 22, true), Ui.Text(hint, 14, false, Ui.Muted));
            var row = new WrapPanel();
            foreach (Resource r in ResourceSet.Types)
            {
                Resource res = r;
                Button b = Ui.Button(res.ToString(), () => onPick(res), minWidth: 90);
                b.Margin = new Thickness(0, 0, 6, 6);
                row.Children.Add(b);
            }
            col.Children.Add(row);
            col.Children.Add(Ui.Button("Back", () => OpenModal(Modal.PlayCard)));
            return Ui.Card(col, 520);
        }

        void PickYear(Resource r)
        {
            if (_yearFirst == Resource.Desert)
            {
                _yearFirst = r;
                BuildOverlay();
                return;
            }
            if (_c.Send(new PlayYearOfPlenty(_c.Game.CurrentPlayer, _yearFirst, r))) CloseModal();
            _yearFirst = Resource.Desert;
        }

        void PickMonopoly(Resource r)
        {
            if (_c.Send(new PlayMonopoly(_c.Game.CurrentPlayer, r))) CloseModal();
        }

        // ---- House rules ---------------------------------------------------------------------------

        Control BuildRules()
        {
            HouseRules d = _draft;
            var col = Ui.Column(7,
                Ui.Text("House rules", 22, true),
                Ui.Text("Changes apply immediately for everyone.", 13, false, Ui.Muted),
                Ui.Stepper("Points to win", d.VictoryPoints, 3, 50, v => d.VictoryPoints = v, 280),
                Ui.Stepper("Discard when over (cards)", d.DiscardThreshold, 1, 50, v => d.DiscardThreshold = v, 280),
                Ui.Stepper("Bank ratio (no port)", d.BankRatio, 2, 6, v => d.BankRatio = v, 280),
                Ui.Stepper("Generic port ratio", d.GenericPortRatio, 2, 6, v => d.GenericPortRatio = v, 280),
                Ui.Stepper("Resource port ratio", d.ResourcePortRatio, 1, 6, v => d.ResourcePortRatio = v, 280),
                Ui.Stepper("Longest road minimum", d.LongestRoadMinimum, 2, 15, v => d.LongestRoadMinimum = v, 280),
                Ui.Stepper("Largest army minimum", d.LargestArmyMinimum, 1, 14, v => d.LargestArmyMinimum = v, 280),
                Ui.Stepper("No 7s for first N rounds", d.NoSevenRounds, 0, 20, v => d.NoSevenRounds = v, 280));

            col.Children.Add(Toggle("Friendly robber (spare players with 2 points or fewer)", d.FriendlyRobber, v => d.FriendlyRobber = v));
            col.Children.Add(Toggle("Only one development card per turn", d.OneDevCardPerTurn, v => d.OneDevCardPerTurn = v));
            col.Children.Add(Toggle("Dev cards playable the turn they're bought", d.PlayDevCardOnPurchaseTurn, v => d.PlayDevCardOnPurchaseTurn = v));

            col.Children.Add(Ui.Row(8,
                Ui.Button("Apply", () =>
                {
                    if (_c.Send(new SetHouseRules(Game.HostPlayer, d))) CloseModal();
                }, primary: true, minWidth: 120),
                Ui.Button("Cancel", CloseModal)));
            return Ui.Card(col, 600);
        }

        static Control Toggle(string text, bool value, Action<bool> set)
        {
            var box = new CheckBox { Content = text, IsChecked = value, Foreground = Palette.Brush(Colors.White) };
            box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
            return box;
        }
    }
}
