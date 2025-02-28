using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace SolutionBuildEvents
{
    public class ConfigurationChangeListener : IVsUpdateSolutionEvents3, IDisposable
    {
        private readonly IVsSolutionBuildManager3 _buildManager;
        private readonly uint _cookie;

        public ConfigurationChangeListener(IServiceProvider serviceProvider)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            _buildManager = serviceProvider.GetService(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager3;
            if (_buildManager != null)
            {
            
                _buildManager.AdviseUpdateSolutionEvents3(this, out _cookie);
            }
        }

        public void Dispose()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (_buildManager != null)
            {
                _buildManager.UnadviseUpdateSolutionEvents3(_cookie);
            }
        }

        public int OnBeforeActiveSolutionCfgChange(IVsCfg pOldActiveSlnCfg, IVsCfg pNewActiveSlnCfg)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterActiveSolutionCfgChange(IVsCfg pOldActiveSlnCfg, IVsCfg pNewActiveSlnCfg)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            pNewActiveSlnCfg.get_DisplayName(out string newConfigName);
            System.Diagnostics.Debug.WriteLine($"Configuration changed to: {newConfigName}");
            ConfigChanged?.Invoke(newConfigName);

            return VSConstants.S_OK;
        }

        public event Action<string> ConfigChanged;
    }
}