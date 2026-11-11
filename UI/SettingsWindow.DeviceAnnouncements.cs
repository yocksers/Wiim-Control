namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private LazyPage? _eqPageRef;
        private LazyPage? _outputPageRef;
        private OutputPage? _outputView;
        private LazyPage? _ampPageRef;

        internal void OnEqualizerChanged(string ip)
        {
            if (_eqPageRef is { IsVisible: true } && _eq.EqIp == ip) _ = _eq.RefreshEqAsync();
        }

        internal void OnOutputChanged(string ip)
        {
            if (_dashboardIp == ip) _ = RefreshDashboardOutputsAsync();
            if (_outputPageRef is { IsVisible: true } && _outputView?.ShowsDevice(ip) == true) _ = _outputView.RefreshOutputAsync(quiet: true);
        }

        internal void OnGroupsChanged()
        {
            if (IsVisible) _ = RefreshMultiroomAsync();
        }

        internal void OnScreenChanged(string ip)
        {
            if (_ampPageRef is { IsVisible: true }) _amp.OnScreenChanged(ip);
        }

        internal void OnInputSignalChanged(string ip) => ApplyDashboardSource(ip);
    }
}
