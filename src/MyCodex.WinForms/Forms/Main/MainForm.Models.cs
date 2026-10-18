using System.Threading.Tasks;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async Task LoadAvailableModelsAsync(bool showStatus, bool ensureConnection = true)
        {
            if (!_isLoadingModels && _modelConfigurationService != null)
            {
                _modelConfigurationService.Invalidate();
            }
            _isLoadingModels = true;
            try
            {
                await RefreshModelConfigurationAsync(_currentWorkspace != null ? _currentWorkspace.RootPath : null,
                    showStatus, ensureConnection);
            }
            finally
            {
                _isLoadingModels = false;
            }
        }
    }
}
