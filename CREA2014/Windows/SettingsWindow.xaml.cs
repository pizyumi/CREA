using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace CREA2014.Windows
{
    public partial class SettingsWindow : Window
    {
        private MainWindow.MainformSettings ms;

        public SettingsWindow(MainWindow.MainformSettings _ms)
        {
            ms = _ms;

            InitializeComponent();

            Title = "設定".Multilanguage(36);
            gbUi.Header = "UI".Multilanguage(37);
            atPortWebSocket.Text = "内部ウェブソケットサーバのポート番号".Multilanguage(38) + "(_P)：";
            atPortWebServer.Text = "内部ウェブサーバのポート番号".Multilanguage(39) + "(_Q)：";
            cbIsWallpaper.Content = "背景画像を表示する".Multilanguage(40) + "(_V)";
            atWallpaper.Text = "背景画像".Multilanguage(41) + "（_W）：";
            bWallpaperBrowse.Content = "参照".Multilanguage(42) + "(_B)...";
            atWallpaperOpacity.Text = "不透明度".Multilanguage(43) + "(_O)：";
            gbOthers.Header = "その他".Multilanguage(44);
            cbConfirmAtExit.Content = "終了確認を行う".Multilanguage(45) + "(_X)";
            bOK.Content = "OK".Multilanguage(46) + "(_O)";
            bCancel.Content = "キャンセル".Multilanguage(47) + "(_C)";

            tbPortWebSocket.Text = ms.PortWebSocket.ToString();
            tbPortWebServer.Text = ms.PortWebServer.ToString();
            cbIsWallpaper.IsChecked = ms.IsWallpaper;
            tbWallpaper.Text = ms.Wallpaper;
            tbWallpaperOpacity.Text = ms.WallpaperOpecity.ToString();
            cbConfirmAtExit.IsChecked = ms.IsConfirmAtExit;
        }

        private void tbPortWebSocket_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void tbPortWebServer_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void tbWallpaper_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void bWallpaperBrowse_Click(object sender, RoutedEventArgs e)
        {
            string imagefile = "image".ExtensionsData();
            string allfile = "all".ExtensionsData();

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = string.Join("|", imagefile, allfile);
            if (ofd.ShowDialog() == true)
                tbWallpaper.Text = ofd.FileName;
        }

        private void tbWallpaperOpacity_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void bOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;

            ms.PortWebSocket = int.Parse(tbPortWebSocket.Text);
            ms.PortWebServer = int.Parse(tbPortWebServer.Text);
            ms.IsWallpaper = (bool)cbIsWallpaper.IsChecked;
            ms.Wallpaper = tbWallpaper.Text;
            ms.WallpaperOpecity = float.Parse(tbWallpaperOpacity.Text);
            ms.IsConfirmAtExit = (bool)cbConfirmAtExit.IsChecked;
        }

        private void bCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}