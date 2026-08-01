using System.Drawing;
using System.Windows.Forms;

namespace gInk
{
    public partial class FormQrDisplay : Form
    {
        public FormQrDisplay(Bitmap qrImage, string configString)
        {
            InitializeComponent();

            pictureBox1.Image = qrImage;
            pictureBox1.SizeMode = PictureBoxSizeMode.Normal;

            lblConfig.Text = configString;
            lblConfig.AutoSize = false;
            lblConfig.TextAlign = ContentAlignment.MiddleCenter;
            lblConfig.Dock = DockStyle.Bottom;
            lblConfig.Height = 60;

            btnClose.DialogResult = DialogResult.OK;

            // Size the form to fit the QR code + label
            int imgSize = qrImage.Width;
            this.ClientSize = new Size(imgSize + 40, imgSize + 100);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
        }
    }
}
