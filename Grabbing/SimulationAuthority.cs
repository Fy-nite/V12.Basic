using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Grabbing
{
    /// <summary>
    /// Client-side helper for grabbing replicated elements. The host is the
    /// authority even for a grabbed object: the client asks to become the
    /// element's simulating user, and the host either grants it (first claim wins)
    /// or ignores it. A granted element ticks, steps and replicates on the
    /// grabbing client; on release the host takes it back.
    /// </summary>
    internal static class SimulationAuthority
    {
        public static WorldSession? SessionFor(GameRoot root, IWorldElement element)
        {
            if (root is null || element is null) return null;
            var world = root.GetWorldForElement(element);
            return world is null ? null : root.FindWorldSession(world);
        }

        public static NetworkWorldLink? LinkFor(GameRoot root, IWorldElement element)
            => SessionFor(root, element)?.Link as NetworkWorldLink;

        /// <summary>
        /// True when another peer (or the host) is simulating this element, so a
        /// local grab has to go through an authority request. False in a solo or
        /// hosted world, where the local peer already simulates everything.
        /// </summary>
        public static bool NeedsClaim(GameRoot root, IWorldElement element)
        {
            var session = SessionFor(root, element);
            return session is not null && !session.IsSimulatedLocally(element);
        }

        /// <summary>
        /// Ask the host for the element. Requires a known player id: without it
        /// the client cannot recognise its own grant, so the claim is pointless.
        /// </summary>
        public static bool Claim(GameRoot root, IWorldElement element)
        {
            var session = SessionFor(root, element);
            if (session is null || session.LocalPlayerId == 0) return false;
            if (session.IsSimulatedLocally(element)) return false;

            return (session.Link as NetworkWorldLink)?.RequestSimulatingUser(element) == true;
        }

        /// <summary>Give the element back, but only if this peer actually owns it.</summary>
        public static void Release(GameRoot root, IWorldElement element)
        {
            var session = SessionFor(root, element);
            if (session is null || session.LocalPlayerId == 0) return;
            if (session.Authorities.GetSimulatingPlayer(session.World, element) != session.LocalPlayerId)
                return;

            (session.Link as NetworkWorldLink)?.ReleaseSimulatingUser(element);
        }
    }
}
