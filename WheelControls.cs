using SDRSharp.PanView;
using System.Windows.Forms;

namespace SDRSharp.InmarsatAero
{
    // Lists that scroll on the mouse wheel mark the wheel event handled, so the parent window does not act on it too (docs 05, 5.2 rule 6)
    internal sealed class ChannelListView : ListView
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e is HandledMouseEventArgs)
            {
                ((HandledMouseEventArgs)e).Handled = true;
            }
        }
    }

    internal sealed class LogTextBox : DoubleBufferedTextBox
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e is HandledMouseEventArgs)
            {
                ((HandledMouseEventArgs)e).Handled = true;
            }
        }
    }
}
