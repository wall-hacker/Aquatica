using UnityEngine;

namespace Aquatica.Cockpit
{
    /// <summary>
    /// Cabin lighting that reacts to faults. The ceiling fixtures are indicator lights that also cast the
    /// cabin's light: warm while everything works, flashing the indicators' danger red while any cockpit
    /// device is faulty. The pressure gauges climb into the red at the same time.
    /// </summary>
    public class CabinAlarm : MonoBehaviour
    {
        [SerializeField] CockpitSystems systems;
        [SerializeField] IndicatorLight[] fixtures;
        [SerializeField] PressureGauge[] gauges;

        public bool IsAlarming { get; private set; }

        void Awake()
        {
            systems.FaultStarted += _ => Refresh();
            systems.Repaired += _ => Refresh();
        }

        void Start() => Refresh();

        void Refresh()
        {
            IsAlarming = systems.ActiveFaults > 0;
            foreach (var fixture in fixtures)
                fixture.SetState(IsAlarming ? IndicatorState.Red : IndicatorState.Warm, IsAlarming);
            foreach (var gauge in gauges)
                gauge.Alarm = IsAlarming;
        }
    }
}
