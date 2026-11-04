using Aquatica.Cockpit;

namespace Aquatica.Navigation
{
    /// <summary>
    /// Something that can take over the <see cref="NavigationStation"/>'s screens for a while (e.g. a prompt or a
    /// readout). The station asks it first every redraw and on every console tap.
    /// </summary>
    public interface IStationOverlay
    {
        /// <summary>Draw the console page (Begin to End) and return true, or return false to leave it to the station.</summary>
        bool DrawConsole(CockpitScreen screen);

        /// <summary>Draw the monitor page (Begin to End) and return true, or return false to leave it to the station.</summary>
        bool DrawMonitor(CockpitScreen screen);

        /// <summary>The console was tapped: return true if the overlay handled it.</summary>
        bool ConsoleTapped();

        /// <summary>Lines shown at the bottom of the monitor's status page (null for none).</summary>
        string[] StatusLines { get; }
    }
}
