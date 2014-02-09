using System;
using System.Windows;
using System.Windows.Media;

namespace CREA2014.Windows
{
    public partial class AboutWindow : Window
    {
        public AboutWindow(string _version)
        {
            InitializeComponent();

            Title = "CREAについて".Multilanguage(23);
            tblVersion.Text = "バージョン".Multilanguage(35);
            tbVersion.Text = _version;
            tblDeveloper.Text = "制作者".Multilanguage(24);
            tbDeveloper.Text = "Piz&Yumina／OSDS開発部".Multilanguage(25);
            tblCopyright.Text = "著作権の所在".Multilanguage(26);
            tbCopyright.Text = "Piz&Yumina（ただし、下記に示すものを除く）".Multilanguage(27);
            tblIconCreater.Text = "CREAアイコン".Multilanguage(28);
            tbIconCreater.Text = "386氏".Multilanguage(29);
            tblCreatanCreater.Text = "CREAたんイラスト".Multilanguage(30);
            tbCreatanCreater.Text = "571氏".Multilanguage(51);
            tblSpecialThanks.Text = "特別協力".Multilanguage(31);
            tbSpecialThanks.Text = "2ch／したらば／PDボード 専用スレ／Twitter　他".Multilanguage(32);
            tbDescription.Text = "本ソフトウェアはソフトウェア使用許諾契約書に示す契約に基づいて実行されています。".Multilanguage(33);
            bOK.Content = "閉じる".Multilanguage(34) + "(_C)";

            tbCrea.FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Resources/#Ubuntu");
            tbCreacoin.FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Resources/#Audiowide");
        }

        private void bOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}