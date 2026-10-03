namespace Catan.View
{
    /// <summary>
    /// Voice chat as the UI sees it, independent of any SDK. Privacy defaults: nothing is joined until the
    /// player asks, and the microphone starts muted.
    /// </summary>
    public interface IVoiceChat
    {
        /// <summary>True once a channel is known and the voice SDK is present.</summary>
        bool Available { get; }

        string Status { get; }
        bool InChannel { get; }

        /// <summary>Open-mic toggle. False (the default) means muted unless push-to-talk is held.</summary>
        bool MicOpen { get; set; }

        void PushToTalk(bool held);

        /// <summary>Whether the player in this seat is currently talking.</summary>
        bool IsSpeaking(int seat);

        /// <summary>Local mute: only affects what this player hears.</summary>
        bool IsMuted(int seat);
        void SetMuted(int seat, bool muted);

        /// <summary>Called when the host hands over the (secret) channel name. Does not join by itself.</summary>
        void Prepare(string channelName, int seat, string displayName);

        void Join();
        void Leave();
    }
}
