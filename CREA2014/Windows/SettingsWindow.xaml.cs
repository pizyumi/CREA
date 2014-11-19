using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace CREA2014.Windows
{
    public partial class SettingsWindow : Window
    {
        private string appname;

        public SettingsWindow(string _appname, Action<SettingsWindow> __Initialize, Action<string> _CreateUiFiles)
        {
            appname = _appname;

            InitializeComponent();

            Title = "設定".Multilanguage(36);
            gbUi.Header = "UI".Multilanguage(37);
            atPortWebSocket.Text = "内部ウェブソケットサーバのポート番号".Multilanguage(38) + "(_P)：";
            atPortWebServer.Text = "内部ウェブサーバのポート番号".Multilanguage(39) + "(_Q)：";
            cbIsWebServerAcceptExternal.Content = "外部からの接続を許可する".Multilanguage(210) + "(_E)";
            cbIsWallpaper.Content = "背景画像を表示する".Multilanguage(40) + "(_V)";
            atWallpaper.Text = "背景画像".Multilanguage(41) + "（_W）：";
            bWallpaperOpen.Content = "ファイルの場所を開く".Multilanguage(154) + "(_Q)...";
            bWallpaperBrowse.Content = "参照".Multilanguage(42) + "(_B)...";
            atWallpaperOpacity.Text = "不透明度".Multilanguage(43) + "(_A)：";
            gbOthers.Header = "その他".Multilanguage(44);
            cbConfirmAtExit.Content = "終了確認を行う".Multilanguage(45) + "(_X)";
            rbDefault.Content = "既定のUIを使用する".Multilanguage(142) + "(_D)";
            rbNotDefault.Content = "独自のUIを使用する".Multilanguage(143) + "(_M)";
            atUiFilesDirectory.Text = "独自UIファイルの保存場所".Multilanguage(144) + "（_F）：";
            bUiFilesOpen.Content = "フォルダを開く".Multilanguage(155) + "(_T)...";
            bUiFilesCreate.Content = "独自UIファイルを作成".Multilanguage(145) + "(_R)";
            bUiFilesDirectoryBrowse.Content = "選択".Multilanguage(146) + "(_S)...";
            bOK.Content = "OK".Multilanguage(46) + "(_O)";
            bCancel.Content = "キャンセル".Multilanguage(47) + "(_C)";

            bUiFilesCreate.Click += (sender, e) =>
            {
                _CreateUiFiles(tbUiFilesDirectory.Text);

                System.Windows.MessageBox.Show("ファイルを生成しました。".Multilanguage(156));
            };

            __Initialize(this);

            SetIsEnabledUiFiles(rbNotDefault.IsChecked.Value);
            SetIsEnabledWallpaper(cbIsWallpaper.IsChecked.Value);

            tbPortWebSocketValidate();
            tbPortWebServerValidate();
            tbWallpaperValidate();
            tbWallpaperOpacityValidate();
            tbUiFilesDirectoryValidate();

            Validate();
        }

        #region 独立

        private void bWallpaperBrowse_Click(object sender, RoutedEventArgs e)
        {
            string imagefile = "image".ExtensionsData();
            string allfile = "all".ExtensionsData();

            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.Filter = string.Join("|", imagefile, allfile);
            if (ofd.ShowDialog() == true)
                tbWallpaper.Text = ofd.FileName;
        }

        private void bUiFilesDirectoryBrowse_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                tbUiFilesDirectory.Text = fbd.SelectedPath;
        }

        private void bWallpaperOpen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(Path.GetDirectoryName(tbWallpaper.Text));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, appname, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void bUiFilesOpen_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(tbUiFilesDirectory.Text);
        }

        private void bOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void bCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        #endregion

        #region 検証

        private bool isValid1;
        private bool isValid2;
        private bool isValid3;
        private bool isValid4;
        private bool isValid5;

        private void Validate()
        {
            if (cbIsWallpaper.IsChecked == true && (!isValid3 || !isValid4))
            {
                bOK.IsEnabled = false;
                return;
            }
            bOK.IsEnabled = isValid1 && isValid2 && (rbDefault.IsChecked == true || isValid5);
        }

        private void tbPortWebSocketValidate()
        {
            ushort ush;
            isValid1 = (ushort.TryParse(tbPortWebSocket.Text, out ush)).Pipe((flag) =>
            {
                tbPortWebSocketChk.Text = flag ? string.Empty : "ポート番号は0～65535までの整数でなければなりません。".Multilanguage(147);
            });
        }

        private void tbPortWebServerValidate()
        {
            ushort ush;
            isValid2 = (ushort.TryParse(tbPortWebServer.Text, out ush)).Pipe((flag) =>
            {
                tbPortWebServerChk.Text = flag ? string.Empty : "ポート番号は0～65535までの整数でなければなりません。".Multilanguage(148);
            });
        }

        private void tbWallpaperValidate()
        {
            isValid3 = (File.Exists(tbWallpaper.Text)).Pipe((flag) =>
            {
                tbWallpaperChk.Text = flag ? string.Empty : "ファイルが存在しません。".Multilanguage(150);

                try
                {
                    bWallpaperOpen.IsEnabled = cbIsWallpaper.IsChecked.Value && Directory.Exists(Path.GetDirectoryName(tbWallpaper.Text));
                }
                catch (Exception)
                {
                    bWallpaperOpen.IsEnabled = false;
                }
            });
        }

        private void tbWallpaperOpacityValidate()
        {
            float flt;
            isValid4 = (float.TryParse(tbWallpaperOpacity.Text, out flt) && flt >= 0.0 && flt <= 1.0).Pipe((flag) =>
            {
                tbWallpaperOpacityChk.Text = flag ? string.Empty : "不透明度は0.0～1.0までの小数でなければなりません。".Multilanguage(149);
            });
        }

        private void tbUiFilesDirectoryValidate()
        {
            isValid5 = (Directory.Exists(tbUiFilesDirectory.Text)).Pipe((flag) =>
            {
                tbUiFilesDirectoryChk.Text = flag ? string.Empty : "フォルダが存在しません。".Multilanguage(151);
                bUiFilesCreate.IsEnabled = rbNotDefault.IsChecked.Value && flag;
                bUiFilesOpen.IsEnabled = rbNotDefault.IsChecked.Value && flag;
            });
        }

        #endregion

        private void tbPortWebSocket_TextChanged(object sender, TextChangedEventArgs e)
        {
            tbPortWebSocketValidate();

            Validate();
        }

        private void tbPortWebServer_TextChanged(object sender, TextChangedEventArgs e)
        {
            tbPortWebServerValidate();

            Validate();
        }

        private void tbWallpaper_TextChanged(object sender, TextChangedEventArgs e)
        {
            tbWallpaperValidate();

            Validate();
        }

        private void tbWallpaperOpacity_TextChanged(object sender, TextChangedEventArgs e)
        {
            tbWallpaperOpacityValidate();

            Validate();
        }

        private void tbUiFilesDirectory_TextChanged(object sender, TextChangedEventArgs e)
        {
            tbUiFilesDirectoryValidate();

            Validate();
        }

        private void SetIsEnabledWallpaper(bool isEnabled)
        {
            tbWallpaper.IsEnabled = isEnabled;
            bWallpaperOpen.IsEnabled = isEnabled;
            bWallpaperBrowse.IsEnabled = isEnabled;
            tbWallpaperOpacity.IsEnabled = isEnabled;
            tbWallpaperChk.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            tbWallpaperOpacityChk.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetIsEnabledUiFiles(bool isEnabled)
        {
            tbUiFilesDirectory.IsEnabled = isEnabled;
            bUiFilesOpen.IsEnabled = isEnabled;
            bUiFilesDirectoryBrowse.IsEnabled = isEnabled;
            bUiFilesCreate.IsEnabled = isEnabled;
            tbUiFilesDirectoryChk.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void rbDefault_Checked(object sender, RoutedEventArgs e)
        {
            SetIsEnabledUiFiles(false);

            Validate();
        }

        private void rbNotDefault_Checked(object sender, RoutedEventArgs e)
        {
            SetIsEnabledUiFiles(true);

            tbUiFilesDirectoryValidate();

            Validate();
        }

        private void cbIsWallpaper_Checked(object sender, RoutedEventArgs e)
        {
            SetIsEnabledWallpaper(cbIsWallpaper.IsChecked.Value);

            if (cbIsWallpaper.IsChecked.Value)
                tbWallpaperValidate();

            Validate();
        }

        private void cbIsWallpaper_Unchecked(object sender, RoutedEventArgs e)
        {
            SetIsEnabledWallpaper(cbIsWallpaper.IsChecked.Value);

            Validate();
        }
    }
}