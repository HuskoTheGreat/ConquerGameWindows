using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Catan.Core.Net;
using Catan.View;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

namespace Catan.Voice
{
    /// <summary>
    /// Voice chat over Vivox. Add this component next to NetworkGameManager; it is picked up automatically.
    ///
    /// Privacy and security
    ///  - Nothing is joined until the player clicks "Join voice chat", and the microphone starts muted
    ///    (push-to-talk on V, or an explicit open-mic toggle).
    ///  - The channel name is the host's 128-bit random secret, delivered only to approved players, so people
    ///    outside the game can't find or join the channel. Names are validated before use.
    ///  - Only a group, audio-only, non-positional channel is ever joined; text stays on the host-relayed path,
    ///    where sender identity can't be spoofed.
    ///  - Who is shown as "talking" is looked up from each participant's display name ("seatN"). A player
    ///    could label themselves as another seat, which only affects that cosmetic indicator, never audio
    ///    routing or game state.
    /// </summary>
    public sealed class VivoxVoice : MonoBehaviour, IVoiceChat
    {
        const string SeatPrefix = "seat";

        string _channel;
        int _seat = -1;
        bool _joining;
        bool _ptt;
        bool _micOpen;
        bool _micApplied = true; // we mute right after joining
        string _status = "";

        readonly HashSet<int> _locallyMuted = new HashSet<int>();
        readonly HashSet<int> _speaking = new HashSet<int>();

        public bool Available => VoiceEnabled && !string.IsNullOrEmpty(_channel);
        public string Status => _status;
        public bool InChannel { get; private set; }

        public bool MicOpen
        {
            get => _micOpen;
            set => _micOpen = value;
        }

        // Set to false to ship without voice, or wire it to a settings toggle.
        public bool VoiceEnabled = true;

        public void Prepare(string channelName, int seat, string displayName)
        {
            // The name comes from the network: never use it unless it is plainly a safe identifier.
            if (!ChatCodec.IsValidChannelName(channelName)) return;
            _channel = channelName;
            _seat = seat;
        }

        public void PushToTalk(bool held) => _ptt = held;
        public bool IsSpeaking(int seat) => _speaking.Contains(seat);
        public bool IsMuted(int seat) => _locallyMuted.Contains(seat);

        public void SetMuted(int seat, bool muted)
        {
            if (muted) _locallyMuted.Add(seat);
            else _locallyMuted.Remove(seat);
            ApplyLocalMute(seat, muted);
        }

        public async void Join() => await JoinAsync();

        public async void Leave() => await LeaveAsync();

        async Task JoinAsync()
        {
            if (_joining || InChannel || !Available) return;
            _joining = true;
            try
            {
                _status = "Connecting voice...";
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();

                await VivoxService.Instance.InitializeAsync();
                if (!VivoxService.Instance.IsLoggedIn)
                    await VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = SeatPrefix + _seat });

                // Audio only, group (non-positional) channel.
                await VivoxService.Instance.JoinGroupChannelAsync(_channel, ChatCapability.AudioOnly);

                VivoxService.Instance.MuteInputDevice(); // start muted
                _micApplied = true;
                InChannel = true;
                _status = "Voice connected.";
            }
            catch (Exception e)
            {
                _status = "Voice failed: " + e.Message;
            }
            finally
            {
                _joining = false;
            }
        }

        async Task LeaveAsync()
        {
            try
            {
                if (VivoxService.Instance.IsLoggedIn) await VivoxService.Instance.LeaveAllChannelsAsync();
            }
            catch (Exception e)
            {
                _status = "Voice error: " + e.Message;
            }
            InChannel = false;
            _speaking.Clear();
            _ptt = false;
            _micOpen = false;
            if (_status.StartsWith("Voice connected")) _status = "";
        }

        void Update()
        {
            if (!InChannel) return;

            // Mic gate: open only if the player toggled it on or is holding push-to-talk.
            bool live = _micOpen || _ptt;
            if (live == _micApplied)
            {
                _micApplied = !live;
                if (live) VivoxService.Instance.UnmuteInputDevice();
                else VivoxService.Instance.MuteInputDevice();
            }

            _speaking.Clear();
            if (!VivoxService.Instance.ActiveChannels.TryGetValue(_channel, out var participants)) return;
            foreach (VivoxParticipant p in participants)
            {
                if (!TryParseSeat(p.DisplayName, out int seat)) continue;
                if (p.SpeechDetected && !p.IsMuted) _speaking.Add(seat);
                if (_locallyMuted.Contains(seat) && !p.IsMuted) ApplyLocalMute(seat, true);
            }
        }

        void ApplyLocalMute(int seat, bool muted)
        {
            if (!InChannel || !VivoxService.Instance.ActiveChannels.TryGetValue(_channel, out var participants)) return;
            foreach (VivoxParticipant p in participants)
            {
                if (p.IsSelf || !TryParseSeat(p.DisplayName, out int s) || s != seat) continue;
                if (muted) p.MutePlayerLocally();
                else p.UnmutePlayerLocally();
            }
        }

        static bool TryParseSeat(string displayName, out int seat)
        {
            seat = -1;
            if (displayName == null || !displayName.StartsWith(SeatPrefix) || displayName.Length != SeatPrefix.Length + 1) return false;
            char c = displayName[SeatPrefix.Length];
            if (c < '0' || c > '5') return false;
            seat = c - '0';
            return true;
        }

        async void OnDestroy()
        {
            try
            {
                if (InChannel) await VivoxService.Instance.LeaveAllChannelsAsync();
            }
            catch (Exception)
            {
            }
        }
    }
}
