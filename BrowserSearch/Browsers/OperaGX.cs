using System.Windows;

namespace BrowserSearch.Browsers
{
    internal class OperaGX : Chromium
    {
        public OperaGX(string userDataDir, string? profileName) : base(userDataDir, null)
        {
            // I don't understand how profiles work on OperaGX nor I could get them to work
            // on the browser itself, so multiple profiles is currently unsupported and untested
            if (profileName is not null)
            {
                MessageBox.Show($"Browser profiles aren't supported on Opera GX", "BrowserSearch");
            }
        }

        protected override void CreateProfiles()
        {
            // Unlike most Chromium-based browsers, OperaGX doesn't have a "Local State" folder
            Profiles["default"] = new ChromiumProfile(UserDataDir);
        }
    }
}
