using System;
using Catan.Core;

namespace Catan.View
{
    /// <summary>
    /// What the UI needs from a network connection, with no dependency on any networking package.
    /// The Netcode adapter implements it; the UI only ever sees a read-only mirror and submits commands.
    /// </summary>
    public interface IGameLink
    {
        /// <summary>This client's seat. Only a convenience for the UI: the host decides the real identity.</summary>
        int Seat { get; }

        /// <summary>The latest host-provided view of the game (null until the game starts).</summary>
        Game Mirror { get; }

        event Action Updated;
        event Action<string> LogReceived;
        event Action<string> ErrorReceived;

        void Submit(Command command);
    }
}
