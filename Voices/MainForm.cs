﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Voices
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private string ErrorLogData = String.Empty;
        private void MainForm_Load(object sender, EventArgs e)
        {
            this.Text = String.Format(this.Text, Define.AppName);
            headLabel.Text = String.Format(headLabel.Text, Define.AppName);
            subDescLabel.Text = String.Format(subDescLabel.Text, Define.AppName);
            RestartKrile.Text = String.Format(RestartKrile.Text, Define.AppName);
            if (Environment.GetCommandLineArgs().Length < 2)
            {
                MessageBox.Show(
                    "このソフトウェアはエラーレポートのためのソフトウェアです。" + Environment.NewLine +
                    "Krileを使うには、krile.exe を起動してください。",
                    "レポーター", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }
            var logFilePath = Environment.GetCommandLineArgs()[1];
            if (File.Exists(logFilePath))
            {
                try
                {
                    ErrorLogData = File.ReadAllText(logFilePath);
                    ErrorLogData += Environment.NewLine + Environment.OSVersion.VersionString;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "ログファイルの読み取りに失敗しました。" + Environment.NewLine +
                        "エラー:" + ex.Message, "レポーター エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("ログファイルが見つかりません。", "レポーター エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            this.Refresh();
            StringBuilder sb = new StringBuilder();
            sb.Append(ErrorLogData);
            sendState.Text = "自己診断中...";
            this.Refresh();
            string err;
            var selfan = SelfAnalyzer.Analyze(ErrorLogData);
            if (selfan != null)
            {
                MessageBox.Show(selfan, "Krile 自己診断", MessageBoxButtons.OK, MessageBoxIcon.Information);
                sendState.Text = "自己診断機能により解決しました。";
                sendProgress.Style = ProgressBarStyle.Continuous;
            }
            else
            {
                sendState.Text = "自己診断できませんでした。データ送信中...";
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        if (!PostString(new Uri("http://krile.starwing.net/report.php"), sb.ToString(), out err))
                        {
                            if (String.IsNullOrEmpty(err))
                                err = "何か不思議な力で失敗しました。もう一度試してみてください。";
                            throw new Exception(err);
                        }
                        this.Invoke(new Action(() =>
                        {
                            sendState.Text = "送信完了しました。";
                            sendProgress.Style = ProgressBarStyle.Continuous;
                            this.Refresh();
                        }));
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(new Action(() =>
                        {
                            MessageBox.Show("アップロードエラー:" + ex.Message);
                            sendState.Text = "送信に失敗しました。";
                            sendProgress.Style = ProgressBarStyle.Continuous;
                        }));
                    }
                });
            }
        }

        private bool PostString(Uri target, string data, out string err)
        {
            err = null;
            //文字コードを指定する
            System.Text.Encoding enc = Encoding.Unicode;

            //POST送信する文字列を作成
            string postData = "error=" + Uri.EscapeDataString(data);

            //バイト型配列に変換
            byte[] postDataBytes = System.Text.Encoding.ASCII.GetBytes(postData);

            //WebRequestの作成
            System.Net.WebRequest req = System.Net.WebRequest.Create(target);

            req.Proxy = System.Net.WebRequest.DefaultWebProxy;

            //メソッドにPOSTを指定
            req.Method = "POST";
            //ContentTypeを"application/x-www-form-urlencoded"にする
            req.ContentType = "application/x-www-form-urlencoded";
            //POST送信するデータの長さを指定
            req.ContentLength = postDataBytes.Length;

            try
            {
                //データをPOST送信するためのStreamを取得
                System.IO.Stream reqStream = req.GetRequestStream();
                //送信するデータを書き込む
                reqStream.Write(postDataBytes, 0, postDataBytes.Length);
                reqStream.Close();

                //サーバーからの応答を受信するためのWebResponseを取得
                System.Net.WebResponse res = req.GetResponse();
                //応答データを受信するためのStreamを取得
                System.IO.Stream resStream = res.GetResponseStream();
                //受信して表示
                System.IO.StreamReader sr = new System.IO.StreamReader(resStream, enc);
                //閉じる
                sr.Close();
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
            return true;
        }

        private void EndKrile_Click(object sender, EventArgs e)
        {
            this.Close();
            Application.Exit();
        }

        private void RestartKrile_Click(object sender, EventArgs e)
        {
            Process.Start(Define.BinName);
            this.Close();
            Application.Exit();
        }

        private void aboutApp_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                Define.AppName + " エラー ヘルパー ツール v" + Define.Version + Environment.NewLine +
                "(C)2011 Karno, starwing network" + Environment.NewLine + Environment.NewLine +
                "このツールについてのお問い合わせも、" + Define.AppName + "と同じ扱いでお願いします。",
                "この機能について", MessageBoxButtons.OK, MessageBoxIcon.Information);
               
        }

        private void errorInfoLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            MessageBox.Show(ErrorLogData, "送信される情報詳細", MessageBoxButtons.OK);
        }
    }
}
