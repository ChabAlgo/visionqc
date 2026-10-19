using System.Drawing;
using System.Windows.Forms;

namespace VisionQC.LocalAgent.Services
{
    internal static class ForegroundMessage
    {
        internal static DialogResult Show(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            using (var owner = new Form { TopMost = true, ShowInTaskbar = false, Opacity = 0,
                StartPosition = FormStartPosition.CenterScreen, Size = new Size(1, 1) })
            {
                owner.Show();
                owner.Activate();
                return MessageBox.Show(owner, text, title, buttons, icon);
            }
        }
    }
}
