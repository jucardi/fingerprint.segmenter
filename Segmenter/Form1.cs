using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.Diagnostics;

namespace Segmenter
{
	public partial class Form1 : Form
	{
		public Form1()
		{
			InitializeComponent();
		}

		private void button1_Click(object sender, EventArgs e)
		{
			if (this.openFileDialog1.ShowDialog() == DialogResult.OK)
			{
				try
				{
					Image image = Image.FromFile(this.openFileDialog1.FileName);
					this.pictureBox1.Image = image;
				}
				catch (System.Exception ex)
				{
					MessageBox.Show(ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private void button2_Click(object sender, EventArgs e)
		{
			if (this.pictureBox1.Image == null)
				return;

			Bitmap bmp = new Bitmap(this.pictureBox1.Image);
			int loops = 25;

			FingerprintSegmenter segmenter = new FingerprintSegmenter(bmp.Width, bmp.Height);
			SegmentInfo[] results = null;
			Image[] images  = null;
			Stopwatch wacth = new Stopwatch();

			wacth.Start();

			for (int i = 0; i < loops; ++i)
				segmenter.Extract(bmp, out results);

			wacth.Stop();

			TimeSpan timeWithoutImages = wacth.Elapsed;

			wacth.Reset();
			wacth.Start();

			for (int i = 0; i < loops; ++i)
				segmenter.Extract(bmp, out results, out images);

			wacth.Stop();

			TimeSpan timeImages = wacth.Elapsed;

			this.pictureBox3.Image = images.Length > 0 ? images[0] : null;
			this.pictureBox4.Image = images.Length > 1 ? images[1] : null;
			this.pictureBox5.Image = images.Length > 2 ? images[2] : null;
			this.pictureBox6.Image = images.Length > 3 ? images[3] : null;

			label1.Text = string.Format("Sin imagenes: {0} segundos - {1} segmentacion/segundo", 0.001 * timeWithoutImages.TotalMilliseconds, (double)loops / (0.001 * timeWithoutImages.TotalMilliseconds));
			label2.Text = string.Format("Con imagenes: {0} segundos - {1} segmentacion/segundo", 0.001 * timeImages.TotalMilliseconds, (double)loops / (0.001 * timeImages.TotalMilliseconds));

			Graphics gfx = Graphics.FromImage(bmp);

			foreach (SegmentInfo r in results)
			{
				Color color = Color.DarkBlue;
				Rectangle rect = new Rectangle(-r.Size.Width / 2, -r.Size.Height / 2, r.Size.Width, r.Size.Height);

				using (Pen pen = new Pen(color, 2))
				{
					gfx.TranslateTransform(r.Centroid.X, r.Centroid.Y);
					gfx.RotateTransform(-r.Rotation);
					gfx.DrawRectangle(pen, rect);
					gfx.ResetTransform();
				}
			}

			gfx.Dispose();

			pictureBox2.Image = bmp;
		}
	}
}
