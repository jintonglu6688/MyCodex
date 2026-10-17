using System;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private bool _isAuthenticationBootstrapOpen;

        private void ShowAuthenticationBootstrapIfRequired()
        {
            if (_isAuthenticationBootstrapOpen || _isAuthenticationCenterOpen || _isRuntimeWindowTransitionInProgress ||
                !MainFormAuthenticationStatePlanner.RequiresOfficialLogin(_authenticationState))
            {
                return;
            }

            if (IsCurrentExecutionTargetWindows())
            {
                OnAuthenticationCenterMenuItemClick(this, EventArgs.Empty);
                return;
            }

            _isAuthenticationBootstrapOpen = true;
            try
            {
                using (var form = new AuthenticationBootstrapForm(_appServerClient, _screenReaderService))
                {
                    form.ShowDialog(this);
                    if (form.AuthenticationState != null && form.AuthenticationState.HasAccount)
                    {
                        _authenticationState = form.AuthenticationState;
                    }
                }
            }
            finally
            {
                _isAuthenticationBootstrapOpen = false;
                RefreshAuthenticationUi();
            }
        }
    }
}
