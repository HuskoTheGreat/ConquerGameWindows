using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Conquer.Client.Animation;
using Conquer.Core;
using Conquer.Core.Bots;

namespace Conquer.Client
{
    public sealed partial class MainWindow
    {
        /// <summary>Shows whichever dialog the current state calls for (or hides the overlay).</summary>
        void BuildOverlay()
        {
            _overlay.Children.Clear();
            UpdateTitle();
            Game g = _c.Game;

            Control content = null;
            if (_modal == Modal.Start && !_offlineOnly) content = BuildStart();
            else if (_modal == Modal.Online) content = BuildOnlineForm();
            else if (_c.IsOnline && _c.Online.Status == Core.Net.OnlineStatus.Disconnected) content = BuildDisconnected();
            else if (_c.IsOnline && g == null) content = BuildLobby();
            else if (g == null || _modal == Modal.Setup || _modal == Modal.Start) content = BuildSetup(g != null);
            else if (_c.HandoffPending) content = BuildHandoff();
            else if (g.Phase == Phase.Discard && (!_c.IsOnline || g.PendingDiscards.ContainsKey(_c.MySeat))) content = BuildDiscard();
            else
            {
                switch (_modal)
                {
                    case Modal.BankTrade: content = BuildBankTrade(); break;
                    case Modal.PlayerTrade: content = BuildPlayerTrade(); break;
                    case Modal.PlayCard: content = BuildPlayCard(); break;
                    case Modal.PickHarvest: content = BuildPickResource("Harvest", _harvestFirst == Resource.Wasteland ? "Pick the first resource" : "Pick the second resource", PickHarvest); break;
                    case Modal.PickPlunder: content = BuildPickResource("Plunder", "Take every card of one resource from the other players", PickPlunder); break;
                    case Modal.Rules: content = BuildRules(); break;
                }
            }

            _overlay.IsVisible = content != null;
            if (content == null) return;

            // The backdrop swallows clicks so the board underneath can't be used by accident. During a hand-off
            // it is fully opaque so the next player can't read the previous player's hand off the screen.
            // Over the title screen the backdrop stays visible: clear behind the menu, lightly dimmed behind forms.
            bool opaque = g != null && _modal != Modal.Setup && _c.HandoffPending;
            Color shade = ShowingTitle ? Color.FromArgb((byte)(_modal == Modal.Start ? 0 : 90), 0, 0, 0)
                : opaque ? Color.FromRgb(0x0c, 0x0e, 0x12) : Color.FromArgb(170, 0, 0, 0);
            _overlay.Children.Add(new Border { Background = Palette.Brush(shade) });
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            _overlay.Children.Add(content);
            if (ShowingTitle) _overlay.Children.Add(VersionStamp());
        }

        // ---- New game ------------------------------------------------------------------------------

        Control BuildSetup(bool canGoBack)
        {
            var tiles = Ui.Text($"{Core.Hex.CountForRadius(_setupRadius)} tiles", 12, false, Ui.Muted);

            var col = Ui.Column(10,
                Ui.Heading("Single player"),
                Ui.Text("Everything runs on this computer. Play against computer players, or pass the device between people.", 13, false, Ui.Muted),
                new Border { Height = 6 },
                Ui.Stepper("Players", _setupPlayers, 2, 6, v => _setupPlayers = v),
                Ui.Stepper("Board radius", _setupRadius, BoardGenerator.MinRadius, 6, v =>
                {
                    _setupRadius = v;
                    tiles.Text = $"{Core.Hex.CountForRadius(v)} tiles";
                }),
                tiles,
                Ui.Stepper("Points to win", _setupVp, 3, 20, v => _setupVp = v));

            col.Children.Add(Ui.Stepper("Computer players", _setupBots, 0, 5, v => _setupBots = v));
            var levelLabel = Ui.Text("Bot level", 14);
            levelLabel.Width = 212;
            levelLabel.VerticalAlignment = VerticalAlignment.Center;
            var levels = Ui.Row(6, levelLabel);
            foreach (BotDifficulty level in new[] { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard })
            {
                BotDifficulty pick = level;
                levels.Children.Add(Ui.Choice(BotPlayer.Describe(level), _setupBotLevel == level, () =>
                {
                    _setupBotLevel = pick;
                    BuildOverlay();
                }));
            }
            col.Children.Add(levels);
            col.Children.Add(Ui.Text("Computer players take the last seats; at least one seat stays human.", 12, false, Ui.Muted));

            var hide = new CheckBox { Content = "Hide hands between turns (when several people share the screen)", IsChecked = _setupHide, Foreground = Palette.Brush(Colors.White) };
            hide.IsCheckedChanged += (_, _) => _setupHide = hide.IsChecked == true;
            col.Children.Add(hide);
            var anim = new CheckBox { Content = "Animations", IsChecked = AnimationLayer.Enabled, Foreground = Palette.Brush(Colors.White) };
            anim.IsCheckedChanged += (_, _) => AnimationLayer.Enabled = anim.IsChecked == true;
            col.Children.Add(anim);
            col.Children.Add(Ui.Text("More options are under House Rules once the game starts.", 12, false, Ui.Muted));
            col.Children.Add(new Border { Height = 6 });

            var buttons = Ui.Row(8, Ui.Button("Start game", StartGame, primary: true, minWidth: 140));
            if (canGoBack) buttons.Children.Add(Ui.Button("Back to game", CloseModal));
            if (!_offlineOnly) buttons.Children.Add(Ui.Button("Back", () => OpenModal(Modal.Start)));
            col.Children.Add(buttons);
            return Ui.Card(col, 480);
        }

        void StartGame()
        {
            _modal = Modal.None;
            // With only one person at the screen there's nobody to hide hands from.
            int people = _setupPlayers - Math.Clamp(_setupBots, 0, _setupPlayers - 1);
            _c.NewGame(_setupPlayers, _setupRadius, _setupVp, _setupHide && people > 1, _setupBots, _setupBotLevel);
            EnsureBotTimer();
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
            return Ui.Card(col, 480);
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
            return Ui.Card(col, 480);
        }

        static ResourceSet ToSet(int[] a) => new ResourceSet(a[0], a[1], a[2], a[3], a[4]);

        // ---- Bank trade ----------------------------------------------------------------------------

        Control BuildBankTrade()
        {
            Game g = _c.Game;
            int me = g.CurrentPlayer;
            var col = Ui.Column(10, Ui.Heading("Bank trade"), Ui.Section("You give"));

            var give = new WrapPanel();
            foreach (Resource r in ResourceSet.Types)
            {
                Resource res = r;
                int ratio = g.GetBankRatio(me, res);
                Button b = Ui.Choice($"{res}\n{ratio}:1 (have {g.Players[me].Hand[res]})", _bankGive == res,
                    () => { _bankGive = res; if (_bankGet == res) _bankGet = Resource.Wasteland; BuildOverlay(); },
                    g.Players[me].Hand[res] >= ratio);
                b.Margin = new Thickness(0, 0, 6, 6);
                give.Children.Add(b);
            }
            col.Children.Add(give);

            col.Children.Add(Ui.Section("You get 1"));
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
                }, _bankGive != Resource.Wasteland && _bankGet != Resource.Wasteland, primary: true, minWidth: 120),
                Ui.Button("Close", CloseModal)));
            return Ui.Card(col, 580);
        }

        // ---- Player trade --------------------------------------------------------------------------

        Control BuildPlayerTrade()
        {
            Game g = _c.Game;
            int me = g.CurrentPlayer;
            var col = Ui.Column(8,
                Ui.Heading("Offer a trade to the table"),
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
            return Ui.Card(col, 660);
        }

        // ---- Action cards ---------------------------------------------------------------------

        Control BuildPlayCard()
        {
            Game g = _c.Game;
            Player me = g.Players[g.CurrentPlayer];
            var col = Ui.Column(8, Ui.Heading("Play an action card"));
            if (g.ActionCardPlayedThisTurn && g.Rules.OneActionCardPerTurn)
                col.Children.Add(Ui.Text("You already played a card this turn.", 13, false, Ui.Muted));

            void Row(ActionCard card, Action play)
            {
                col.Children.Add(Ui.Button($"{card}  (x{me.ActionCardsUsable(card)})", play, me.ActionCardsUsable(card) > 0, minWidth: 260));
            }

            Row(ActionCard.Soldier, () => { if (_c.Send(new PlaySoldier(me.Id))) CloseModal(); });
            Row(ActionCard.Engineers, () => { if (_c.Send(new PlayEngineers(me.Id))) CloseModal(); });
            Row(ActionCard.Harvest, () => { _harvestFirst = Resource.Wasteland; OpenModal(Modal.PickHarvest); });
            Row(ActionCard.Plunder, () => OpenModal(Modal.PickPlunder));

            col.Children.Add(Ui.Text($"Victory Point cards in hand: {me.ActionCardsTotal(ActionCard.VictoryPoint)} (they count automatically)", 12, false, Ui.Muted));
            col.Children.Add(Ui.Button("Close", CloseModal));
            return Ui.Card(col, 440);
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
            return Ui.Card(col, 540);
        }

        void PickHarvest(Resource r)
        {
            if (_harvestFirst == Resource.Wasteland)
            {
                _harvestFirst = r;
                BuildOverlay();
                return;
            }
            if (_c.Send(new PlayHarvest(_c.Game.CurrentPlayer, _harvestFirst, r))) CloseModal();
            _harvestFirst = Resource.Wasteland;
        }

        void PickPlunder(Resource r)
        {
            if (_c.Send(new PlayPlunder(_c.Game.CurrentPlayer, r))) CloseModal();
        }

        // ---- House rules ---------------------------------------------------------------------------

        Control BuildRules()
        {
            HouseRules d = _draft;
            var col = Ui.Column(7,
                Ui.Heading("House rules"),
                Ui.Text("Changes apply immediately for everyone.", 13, false, Ui.Muted),
                Ui.Stepper("Points to win", d.VictoryPoints, 3, 50, v => d.VictoryPoints = v, 280),
                Ui.Stepper("Discard when over (cards)", d.DiscardThreshold, 1, 50, v => d.DiscardThreshold = v, 280),
                Ui.Stepper("Bank ratio (no port)", d.BankRatio, 2, 6, v => d.BankRatio = v, 280),
                Ui.Stepper("Generic port ratio", d.GenericPortRatio, 2, 6, v => d.GenericPortRatio = v, 280),
                Ui.Stepper("Resource port ratio", d.ResourcePortRatio, 1, 6, v => d.ResourcePortRatio = v, 280),
                Ui.Stepper("Great road minimum", d.GreatRoadMinimum, 2, 15, v => d.GreatRoadMinimum = v, 280),
                Ui.Stepper("Grand army minimum", d.GrandArmyMinimum, 1, 14, v => d.GrandArmyMinimum = v, 280),
                Ui.Stepper("No 7s for first N rounds", d.NoSevenRounds, 0, 20, v => d.NoSevenRounds = v, 280));

            col.Children.Add(Toggle("Friendly raider (spare players with 2 points or fewer)", d.FriendlyRaider, v => d.FriendlyRaider = v));
            col.Children.Add(Toggle("Only one action card per turn", d.OneActionCardPerTurn, v => d.OneActionCardPerTurn = v));
            col.Children.Add(Toggle("Action cards playable the turn they're bought", d.PlayActionCardOnPurchaseTurn, v => d.PlayActionCardOnPurchaseTurn = v));
            col.Children.Add(Toggle("Trade anytime (players and bank, even on others' turns)", d.TradeAnytime, v => d.TradeAnytime = v));

            // The deck and starting hands are fixed at the first roll.
            Phase phase = _c.Game.Phase;
            bool beforeFirstRoll = phase == Phase.SetupVillage || phase == Phase.SetupRoad;
            if (beforeFirstRoll)
                col.Children.Add(Ui.Stepper("Starting cards of each resource", d.StartingResources, 0, 5, v => d.StartingResources = v, 280));
            col.Children.Add(new Border { Height = 4 });
            col.Children.Add(Ui.Section("Effect cards (shuffled into the action deck)"));
            if (beforeFirstRoll)
            {
                var grid = new WrapPanel { Orientation = Orientation.Horizontal };
                for (int i = 0; i < EffectCardInfo.Count; i++)
                {
                    int index = i;
                    var e = (EffectCard)i;
                    var cell = Ui.Stepper(EffectCardInfo.Name(e), d.EffectCards[index], 0, EffectCardInfo.MaxEach,
                        v => d.EffectCards[index] = v, 110);
                    ToolTip.SetTip(cell, EffectCardInfo.Description(e));
                    cell.Margin = new Thickness(0, 0, 18, 6);
                    grid.Children.Add(cell);
                }
                col.Children.Add(grid);
                col.Children.Add(Ui.Text("Hover a card for what it does. These lock in at the first roll.", 12, false, Ui.Muted));
            }
            else
            {
                var chosen = Enumerable.Range(0, EffectCardInfo.Count)
                    .Where(i => d.EffectCards[i] > 0)
                    .Select(i => $"{d.EffectCards[i]} {EffectCardInfo.Name((EffectCard)i)}")
                    .ToList();
                col.Children.Add(Ui.Text(chosen.Count == 0 ? "None in this game." : string.Join(", ", chosen) + ".", 13));
                col.Children.Add(Ui.Text($"Starting cards: {d.StartingResources} of each. Effect cards and starting cards can only change before the first roll.", 12, false, Ui.Muted));
            }

            col.Children.Add(Ui.Row(8,
                Ui.Button("Apply", () =>
                {
                    if (_c.Send(new SetHouseRules(Game.HostPlayer, d))) CloseModal();
                }, primary: true, minWidth: 120),
                Ui.Button("Cancel", CloseModal)));
            // Tall now, so it scrolls on small windows.
            return Ui.Card(new ScrollViewer { Content = col, MaxHeight = Math.Max(400, ClientSize.Height - 140) }, 620);
        }

        static Control Toggle(string text, bool value, Action<bool> set)
        {
            var box = new CheckBox { Content = text, IsChecked = value, Foreground = Palette.Brush(Colors.White) };
            box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
            return box;
        }
    }
}
