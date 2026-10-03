using System.Collections.Generic;
using System.Linq;
using Catan.Core;
using UnityEngine;

namespace Catan.View
{
    public sealed partial class HotSeatController
    {
        // IMGUI layout works in pixels, so everything scales from a 720p baseline.
        float _u = 1f;
        GUIStyle _label, _button, _box, _title, _toastStyle;
        float _styleScale = -1f;
        readonly List<Rect> _uiRects = new List<Rect>();

        // Modal working state
        Resource _bankGive = Resource.Desert, _bankGet = Resource.Desert;
        readonly int[] _offerGive = new int[5];
        readonly int[] _offerWant = new int[5];
        readonly int[] _discardSel = new int[5];
        int _discardFor = -1;
        Resource _yearFirst = Resource.Desert;
        HouseRules _draft;

        // Chat
        readonly List<(int Seat, string Text)> _chat = new List<(int, string)>();
        string _chatInput = "";
        GUIStyle _field;

        void EnsureStyles()
        {
            _u = Mathf.Max(1f, Screen.height / 720f);
            if (_label != null && Mathf.Approximately(_styleScale, _u)) return;
            _styleScale = _u;

            _label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * _u), richText = true, wordWrap = true };
            _title = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(19 * _u), fontStyle = FontStyle.Bold };
            _button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(15 * _u), richText = true, wordWrap = true };
            _box = new GUIStyle(GUI.skin.box) { padding = new RectOffset((int)(10 * _u), (int)(10 * _u), (int)(8 * _u), (int)(8 * _u)) };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(15 * _u) };
            _toastStyle = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(17 * _u), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        void OnGUI()
        {
            EnsureStyles();
            _uiRects.Clear();

            if (Remote && _game == null) return; // the network lobby draws its own screen

            if (_game == null || _mode == Mode.Setup)
            {
                DrawSetup();
                return;
            }

            if (_handoff)
            {
                DrawHandoff();
                return;
            }

            bool discarding = _game.Phase == Phase.Discard && HasTurn;
            if (!HasTurn) _mode = Mode.None;

            GUI.enabled = _mode == Mode.None && !discarding;
            DrawStatus();
            DrawLog();
            GUI.enabled = _mode == Mode.None && !discarding && HasTurn;
            DrawActionBar();
            GUI.enabled = _mode == Mode.None && !discarding;
            DrawTradeOffer();
            GUI.enabled = true;

            if (discarding) DrawDiscard();
            else
            {
                switch (_mode)
                {
                    case Mode.BankTrade: DrawBankTrade(); break;
                    case Mode.PlayerTrade: DrawPlayerTrade(); break;
                    case Mode.PlayCard: DrawPlayCard(); break;
                    case Mode.PickYearOfPlenty: DrawPickResource("Year of Plenty", _yearFirst == Resource.Desert ? "Pick the first resource" : "Pick the second resource", PickYear); break;
                    case Mode.PickMonopoly: DrawPickResource("Monopoly", "Take every card of one resource", PickMonopoly); break;
                    case Mode.Rules: DrawRules(); break;
                }
            }

            if (Remote) DrawChat();
            DrawToast();
            HandleBoardInput();
        }

        // ---- Board clicking ------------------------------------------------------------------------

        void HandleBoardInput()
        {
            Event e = Event.current;
            if (_mode != Mode.None || _game.Phase == Phase.Discard || _candidates.Count == 0) return;
            if (OverUi(e.mousePosition)) return;

            if (e.type == EventType.Repaint)
            {
                int hover = PickCandidate(e.mousePosition);
                if (hover != _hover)
                {
                    _hover = hover;
                    _pieces.SetHover(hover, _flat);
                }
            }
            else if (e.type == EventType.MouseDown && e.button == 0)
            {
                int pick = PickCandidate(e.mousePosition);
                if (pick >= 0)
                {
                    ClickCandidate(_candidates[pick]);
                    e.Use();
                }
            }
        }

        bool OverUi(Vector2 p)
        {
            foreach (Rect r in _uiRects) if (r.Contains(p)) return true;
            return false;
        }

        // ---- Small widgets -------------------------------------------------------------------------

        void BeginPanel(Rect rect)
        {
            _uiRects.Add(rect);
            GUILayout.BeginArea(rect, _box);
        }

        bool Btn(string text, bool enabled = true, float width = 0f)
        {
            bool prev = GUI.enabled;
            GUI.enabled = prev && enabled;
            bool clicked = width > 0f
                ? GUILayout.Button(text, _button, GUILayout.Height(32 * _u), GUILayout.Width(width))
                : GUILayout.Button(text, _button, GUILayout.Height(32 * _u));
            GUI.enabled = prev;
            return clicked;
        }

        int Stepper(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label, GUILayout.Width(190 * _u));
            if (Btn("-", value > min, 34 * _u)) value--;
            GUILayout.Label(value.ToString(), _label, GUILayout.Width(34 * _u));
            if (Btn("+", value < max, 34 * _u)) value++;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, min, max);
        }

        Rect Centered(float width, float height) =>
            new Rect((Screen.width - width * _u) / 2f, (Screen.height - height * _u) / 2f, width * _u, height * _u);

        string ResText(Resource r) => $"<color=#{HtmlColor(BoardPalette.For(r))}>{r}</color>";

        static string Cards(ResourceSet s) =>
            string.Join(" ", ResourceSet.Types.Where(r => s[r] > 0).Select(r => $"{s[r]} {r}"));

        // ---- Status, log, toast --------------------------------------------------------------------

        void DrawStatus()
        {
            Player me = ActorPlayer;
            BeginPanel(new Rect(10 * _u, 10 * _u, 330 * _u, 470 * _u));

            GUILayout.Label($"{Dot(me.Id)} <b>{me.Name}</b>   (turn {_game.Turn})", _title);
            GUILayout.Label(Prompt(), _label);
            if (_game.LastRoll > 0 && _game.Phase != Phase.Roll) GUILayout.Label($"Last roll: <b>{_game.LastRoll}</b>", _label);
            GUILayout.Space(6 * _u);

            foreach (Player p in _game.Players)
            {
                bool mine = p.Id == me.Id;
                int vp = mine ? _game.VictoryPoints(p.Id) : _game.PublicVictoryPoints(p.Id);
                string badges = (_game.LongestRoadHolder == p.Id ? " [Road]" : "") + (_game.LargestArmyHolder == p.Id ? " [Army]" : "");
                if (Remote && _link.Voice != null && _link.Voice.IsSpeaking(p.Id)) badges += " <color=#7CFC00>(talking)</color>";
                GUILayout.Label(
                    $"{Dot(p.Id)} {(mine ? "<b>" + p.Name + "</b>" : p.Name)}  VP {vp}/{_game.Rules.VictoryPoints}  " +
                    $"cards {p.HandCount}  dev {p.DevCardCount}  knights {p.KnightsPlayed}  road {p.LongestRoad}{badges}", _label);
            }

            GUILayout.Space(6 * _u);
            GUILayout.Label("<b>Your hand</b>", _label);
            GUILayout.Label(string.Join("   ", ResourceSet.Types.Select(r => $"{ResText(r)} {me.Hand[r]}")), _label);

            string dev = string.Join(", ", ((DevCard[])System.Enum.GetValues(typeof(DevCard)))
                .Where(c => me.DevCardsTotal(c) > 0)
                .Select(c => $"{c} x{me.DevCardsTotal(c)}" + (me.DevCardsUsable(c) < me.DevCardsTotal(c) ? " (new)" : "")));
            GUILayout.Label("Dev cards: " + (dev.Length == 0 ? "none" : dev), _label);

            GUILayout.Space(6 * _u);
            GUILayout.Label($"Bank: {Cards(_game.Bank)}   Deck: {_game.DevDeckCount}", _label);
            GUILayout.EndArea();
        }

        // ---- Chat and voice ------------------------------------------------------------------------

        const string ChatField = "catan_chat";

        void DrawChat()
        {
            IVoiceChat voice = _link.Voice;
            Event e = Event.current;
            bool typing = GUI.GetNameOfFocusedControl() == ChatField;

            // Push-to-talk on V, but never while typing a message.
            if (voice != null && voice.InChannel)
            {
                if (typing) voice.PushToTalk(false);
                else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.V) voice.PushToTalk(true);
                else if (e.type == EventType.KeyUp && e.keyCode == KeyCode.V) voice.PushToTalk(false);
            }

            if (typing && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                SubmitChat();
                e.Use();
            }

            float h = 250 * _u;
            BeginPanel(new Rect(10 * _u, Screen.height - h - 10 * _u, 340 * _u, h));

            if (voice != null && voice.Available)
            {
                GUILayout.BeginHorizontal();
                if (!voice.InChannel)
                {
                    if (Btn("Join voice chat")) voice.Join();
                }
                else
                {
                    if (Btn(voice.MicOpen ? "<b>Mic: LIVE</b>" : "Mic: muted (hold V)")) voice.MicOpen = !voice.MicOpen;
                    if (Btn("Leave", true, 80 * _u)) voice.Leave();
                }
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(voice.Status)) GUILayout.Label(voice.Status, _label);
            }

            foreach ((int seat, string text) in _chat.Skip(System.Math.Max(0, _chat.Count - 5)))
            {
                string who = seat >= 0 && seat < _game.Players.Count ? _game.Players[seat].Name : "?";
                GUILayout.Label($"{Dot(seat)} <b>{who}</b>: {text}", _label);
            }
            GUILayout.FlexibleSpace();

            // Moderation and per-player voice mute, compact: one toggle per other player.
            bool host = _link.IsHost;
            bool inVoice = voice != null && voice.InChannel;
            if (host || inVoice)
            {
                GUILayout.BeginHorizontal();
                foreach (Player p in _game.Players)
                {
                    if (p.Id == _link.Seat) continue;
                    if (host)
                    {
                        bool muted = _link.IsChatMuted(p.Id);
                        if (Btn(muted ? $"Unmute {p.Name}" : $"Mute {p.Name}", true, 0f)) _link.SetChatMuted(p.Id, !muted);
                    }
                    else if (inVoice)
                    {
                        bool muted = voice.IsMuted(p.Id);
                        if (Btn(muted ? $"Unmute {p.Name}" : $"Mute {p.Name}", true, 0f)) voice.SetMuted(p.Id, !muted);
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(ChatField);
            _chatInput = GUILayout.TextField(_chatInput, ChatMax, _field, GUILayout.Height(30 * _u));
            if (Btn("Send", !string.IsNullOrWhiteSpace(_chatInput), 70 * _u)) SubmitChat();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        const int ChatMax = 200;

        void SubmitChat()
        {
            if (string.IsNullOrWhiteSpace(_chatInput)) return;
            _link.SendChat(_chatInput);
            _chatInput = "";
        }

        void DrawLog()
        {
            float w = 340 * _u;
            BeginPanel(new Rect(Screen.width - w - 10 * _u, 10 * _u, w, 300 * _u));
            GUILayout.Label("<b>Game log</b>", _label);
            foreach (string line in _log.Skip(System.Math.Max(0, _log.Count - 11))) GUILayout.Label(line, _label);
            GUILayout.EndArea();
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(_toast) || Time.realtimeSinceStartup > _toastUntil) return;
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.55f, 0.5f);
            GUI.Label(new Rect(Screen.width / 2f - 300 * _u, 20 * _u, 600 * _u, 34 * _u), _toast, _toastStyle);
            GUI.color = prev;
        }

        // ---- Action bar ----------------------------------------------------------------------------

        void DrawActionBar()
        {
            float w = Mathf.Min(Screen.width - 380 * _u - 380 * _u, 860 * _u);
            var rect = new Rect((Screen.width - w) / 2f, Screen.height - 130 * _u, w, 120 * _u);
            BeginPanel(rect);

            if (!HasTurn && _game.Phase != Phase.GameOver)
                GUILayout.Label($"Waiting for {_game.Players[_game.CurrentPlayer].Name}...", _label);

            GUILayout.BeginHorizontal();
            switch (HasTurn ? _game.Phase : Phase.GameOver)
            {
                case Phase.Roll:
                    if (Btn("<b>Roll Dice</b>")) Send(new RollDice(_game.CurrentPlayer));
                    if (ActorPlayer.DevCardsUsable(DevCard.Knight) > 0 && Btn("Play Knight first"))
                        Send(new PlayKnight(_game.CurrentPlayer));
                    break;

                case Phase.Steal:
                    foreach (int id in _game.StealCandidates)
                    {
                        if (Btn($"Steal from {_game.Players[id].Name} ({_game.Players[id].HandCount} cards)"))
                            Send(new StealFrom(_game.CurrentPlayer, id));
                    }
                    break;

                case Phase.Main:
                    DrawMainButtons();
                    break;

                case Phase.GameOver:
                    if (!Remote && Btn("New Game")) _mode = Mode.Setup;
                    break;

                default:
                    GUILayout.Label(Prompt(), _label);
                    break;
            }
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (!Remote || _link.Seat == Game.HostPlayer)
            {
                if (Btn("House Rules", true, 150 * _u))
                {
                    _draft = _game.Rules.Clone();
                    _mode = Mode.Rules;
                }
            }
            if (!Remote && Btn("New Game", true, 150 * _u)) _mode = Mode.Setup;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawMainButtons()
        {
            Player me = _game.Players[_game.CurrentPlayer];
            string Mark(Tool t, string text) => _tool == t ? $"<b>[{text}]</b>" : text;

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            if (Btn(Mark(Tool.Road, "Road\n1 Wood 1 Brick"), me.RoadsLeft > 0)) SelectTool(Tool.Road, Costs.Road);
            if (Btn(Mark(Tool.Settlement, "Settlement\n1 Wood Brick Sheep Wheat"), me.SettlementsLeft > 0)) SelectTool(Tool.Settlement, Costs.Settlement);
            if (Btn(Mark(Tool.City, "City\n2 Wheat 3 Ore"), me.CitiesLeft > 0)) SelectTool(Tool.City, Costs.City);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (Btn("Buy Dev Card", me.Hand.Contains(Costs.DevCard) && _game.DevDeckCount > 0)) Send(new BuyDevCard(me.Id));
            if (Btn("Bank Trade")) { _bankGive = _bankGet = Resource.Desert; _mode = Mode.BankTrade; }
            if (Btn("Trade Players")) { System.Array.Clear(_offerGive, 0, 5); System.Array.Clear(_offerWant, 0, 5); _mode = Mode.PlayerTrade; }
            if (Btn("Play Card")) _mode = Mode.PlayCard;
            if (Btn("<b>End Turn</b>")) { _tool = Tool.None; Send(new EndTurn(me.Id)); }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        // ---- Open trade offer ----------------------------------------------------------------------

        void DrawTradeOffer()
        {
            TradeOffer offer = _game.PendingTrade;
            if (offer == null || _game.Phase != Phase.Main) return;

            BeginPanel(new Rect((Screen.width - 520 * _u) / 2f, Screen.height - 260 * _u, 520 * _u, 120 * _u));
            GUILayout.Label($"{Dot(offer.From)} {_game.Players[offer.From].Name} offers <b>{Cards(offer.Give)}</b> for <b>{Cards(offer.Want)}</b>", _label);
            GUILayout.BeginHorizontal();
            foreach (Player p in _game.Players)
            {
                if (p.Id == offer.From) continue;
                if (Remote && p.Id != _link.Seat) continue; // online you can only answer for yourself
                if (Btn(Remote ? "Accept" : $"Accept as {p.Name}", p.Hand.Contains(offer.Want))) Send(new AcceptTrade(p.Id));
            }
            if ((!Remote || offer.From == _link.Seat) && Btn("Withdraw")) Send(new CancelTrade(offer.From));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ---- Modals --------------------------------------------------------------------------------

        void DrawSetup()
        {
            BeginPanel(Centered(480, 420));
            GUILayout.Label("Catan: New Game", _title);
            playerCount = Stepper("Players", playerCount, 2, 6);
            radius = Stepper($"Board radius ({Core.Hex.CountForRadius(radius)} tiles)", radius, BoardGenerator.MinRadius, 6);
            victoryPoints = Stepper("Points to win", victoryPoints, 3, 20);
            hideHandsBetweenTurns = GUILayout.Toggle(hideHandsBetweenTurns, " Hide hands between turns (pass-and-play)", _label);
            GUILayout.Space(10 * _u);
            GUILayout.Label("More options are available under House Rules once the game starts.", _label);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (Btn("<b>Start Game</b>")) NewGame();
            if (_game != null && Btn("Back to game", true, 150 * _u)) _mode = Mode.None;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawHandoff()
        {
            string who = _game.Phase == Phase.GameOver ? "" : _game.Players[Actor].Name;
            if (GUI.Button(new Rect(0, 0, Screen.width, Screen.height),
                    $"Pass the device to\n<size={Mathf.RoundToInt(40 * _u)}>{Dot(Actor)} <b>{who}</b></size>\n\nClick to continue", _button))
                _handoff = false;
        }

        void DrawBankTrade()
        {
            BeginPanel(Centered(560, 360));
            int me = _game.CurrentPlayer;
            GUILayout.Label("Bank trade", _title);

            GUILayout.Label("Give:", _label);
            GUILayout.BeginHorizontal();
            foreach (Resource r in ResourceSet.Types)
            {
                int ratio = _game.GetBankRatio(me, r);
                bool can = _game.Players[me].Hand[r] >= ratio;
                if (Btn((_bankGive == r ? "<b>[" : "") + $"{r}\n{ratio}:1 (have {_game.Players[me].Hand[r]})" + (_bankGive == r ? "]</b>" : ""), can)) _bankGive = r;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Get 1:", _label);
            GUILayout.BeginHorizontal();
            foreach (Resource r in ResourceSet.Types)
            {
                if (Btn((_bankGet == r ? "<b>[" : "") + r + (_bankGet == r ? "]</b>" : ""), _game.Bank[r] > 0 && r != _bankGive)) _bankGet = r;
            }
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (Btn("<b>Trade</b>", _bankGive != Resource.Desert && _bankGet != Resource.Desert) &&
                Send(new BankTrade(me, _bankGive, _bankGet))) _mode = Mode.None;
            if (Btn("Close")) _mode = Mode.None;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawPlayerTrade()
        {
            BeginPanel(Centered(520, 420));
            int me = _game.CurrentPlayer;
            GUILayout.Label("Offer a trade to the table", _title);
            GUILayout.Label("Other players can accept it from the offer panel.", _label);

            for (int i = 0; i < 5; i++)
            {
                Resource r = ResourceSet.Types[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{ResText(r)} (have {_game.Players[me].Hand[r]})", _label, GUILayout.Width(150 * _u));
                GUILayout.Label("give", _label, GUILayout.Width(40 * _u));
                if (Btn("-", _offerGive[i] > 0, 30 * _u)) _offerGive[i]--;
                GUILayout.Label(_offerGive[i].ToString(), _label, GUILayout.Width(26 * _u));
                if (Btn("+", _offerGive[i] < _game.Players[me].Hand[r], 30 * _u)) _offerGive[i]++;
                GUILayout.Label("want", _label, GUILayout.Width(44 * _u));
                if (Btn("-", _offerWant[i] > 0, 30 * _u)) _offerWant[i]--;
                GUILayout.Label(_offerWant[i].ToString(), _label, GUILayout.Width(26 * _u));
                if (Btn("+", _offerWant[i] < 19, 30 * _u)) _offerWant[i]++;
                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (Btn("<b>Propose</b>") &&
                Send(new ProposeTrade(me, ToSet(_offerGive), ToSet(_offerWant)))) _mode = Mode.None;
            if (Btn("Close")) _mode = Mode.None;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static ResourceSet ToSet(int[] a) => new ResourceSet(a[0], a[1], a[2], a[3], a[4]);

        void DrawPlayCard()
        {
            BeginPanel(Centered(420, 330));
            Player me = _game.Players[_game.CurrentPlayer];
            GUILayout.Label("Play a development card", _title);
            if (_game.DevCardPlayedThisTurn && _game.Rules.OneDevCardPerTurn)
                GUILayout.Label("You already played a card this turn.", _label);

            DevCard[] playable = { DevCard.Knight, DevCard.RoadBuilding, DevCard.YearOfPlenty, DevCard.Monopoly };
            foreach (DevCard c in playable)
            {
                if (!Btn($"{c}  (x{me.DevCardsUsable(c)})", me.DevCardsUsable(c) > 0)) continue;
                switch (c)
                {
                    case DevCard.Knight: if (Send(new PlayKnight(me.Id))) _mode = Mode.None; break;
                    case DevCard.RoadBuilding: if (Send(new PlayRoadBuilding(me.Id))) _mode = Mode.None; break;
                    case DevCard.YearOfPlenty: _yearFirst = Resource.Desert; _mode = Mode.PickYearOfPlenty; break;
                    case DevCard.Monopoly: _mode = Mode.PickMonopoly; break;
                }
            }
            GUILayout.Label($"Victory Point cards in hand: {me.DevCardsTotal(DevCard.VictoryPoint)} (they count automatically)", _label);
            GUILayout.FlexibleSpace();
            if (Btn("Close")) _mode = Mode.None;
            GUILayout.EndArea();
        }

        void DrawPickResource(string title, string hint, System.Action<Resource> onPick)
        {
            BeginPanel(Centered(560, 230));
            GUILayout.Label(title, _title);
            GUILayout.Label(hint, _label);
            GUILayout.BeginHorizontal();
            foreach (Resource r in ResourceSet.Types)
            {
                if (Btn(r.ToString())) onPick(r);
            }
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (Btn("Cancel")) _mode = Mode.PlayCard;
            GUILayout.EndArea();
        }

        void PickYear(Resource r)
        {
            if (_yearFirst == Resource.Desert)
            {
                _yearFirst = r;
                return;
            }
            if (Send(new PlayYearOfPlenty(_game.CurrentPlayer, _yearFirst, r))) _mode = Mode.None;
            _yearFirst = Resource.Desert;
        }

        void PickMonopoly(Resource r)
        {
            if (Send(new PlayMonopoly(_game.CurrentPlayer, r))) _mode = Mode.None;
        }

        void DrawDiscard()
        {
            int who = Actor;
            if (who != _discardFor)
            {
                System.Array.Clear(_discardSel, 0, 5);
                _discardFor = who;
            }

            Player p = _game.Players[who];
            int owe = _game.PendingDiscards[who];
            BeginPanel(Centered(480, 380));
            GUILayout.Label($"{Dot(who)} {p.Name}: discard {owe} cards", _title);
            GUILayout.Label("A 7 was rolled and you hold too many cards.", _label);

            for (int i = 0; i < 5; i++)
            {
                Resource r = ResourceSet.Types[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{ResText(r)} (have {p.Hand[r]})", _label, GUILayout.Width(190 * _u));
                if (Btn("-", _discardSel[i] > 0, 34 * _u)) _discardSel[i]--;
                GUILayout.Label(_discardSel[i].ToString(), _label, GUILayout.Width(34 * _u));
                if (Btn("+", _discardSel[i] < p.Hand[r] && _discardSel.Sum() < owe, 34 * _u)) _discardSel[i]++;
                GUILayout.EndHorizontal();
            }

            GUILayout.Label($"Selected {_discardSel.Sum()} of {owe}", _label);
            GUILayout.FlexibleSpace();
            if (Btn("<b>Discard</b>", _discardSel.Sum() == owe) && Send(new DiscardCards(who, ToSet(_discardSel))))
            {
                System.Array.Clear(_discardSel, 0, 5);
                _discardFor = -1;
            }
            GUILayout.EndArea();
        }

        void DrawRules()
        {
            BeginPanel(Centered(560, 640));
            GUILayout.Label("House Rules", _title);
            GUILayout.Label("Changes apply to everyone immediately.", _label);

            HouseRules d = _draft;
            d.VictoryPoints = Stepper("Points to win", d.VictoryPoints, 3, 50);
            d.DiscardThreshold = Stepper("Discard when over (cards)", d.DiscardThreshold, 1, 50);
            d.BankRatio = Stepper("Bank ratio (no port)", d.BankRatio, 2, 6);
            d.GenericPortRatio = Stepper("Generic port ratio", d.GenericPortRatio, 2, d.BankRatio);
            d.ResourcePortRatio = Stepper("Resource port ratio", d.ResourcePortRatio, 1, d.GenericPortRatio);
            d.LongestRoadMinimum = Stepper("Longest road minimum", d.LongestRoadMinimum, 2, 15);
            d.LargestArmyMinimum = Stepper("Largest army minimum", d.LargestArmyMinimum, 1, 14);
            d.NoSevenRounds = Stepper("No 7s for first N rounds", d.NoSevenRounds, 0, 20);
            d.FriendlyRobber = GUILayout.Toggle(d.FriendlyRobber, " Friendly robber (spare players with 2 points or fewer)", _label);
            d.OneDevCardPerTurn = GUILayout.Toggle(d.OneDevCardPerTurn, " Only one development card per turn", _label);
            d.PlayDevCardOnPurchaseTurn = GUILayout.Toggle(d.PlayDevCardOnPurchaseTurn, " Dev cards playable the turn they're bought", _label);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (Btn("<b>Apply</b>") && Send(new SetHouseRules(Game.HostPlayer, d))) _mode = Mode.None;
            if (Btn("Cancel")) _mode = Mode.None;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
