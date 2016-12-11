using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

/*
ファイル転送の仕様
　UIとか
　　メッセージとかと同じようにターゲットを自由に選択できる
　　進捗状況の表示(プログレスバーと現在のファイル名とか)
　　受信の開始と完了時に通知を出す
　保存方法
　　個別に名前を付けて保存
　　複数選択してフォルダを指定し保存
　　あらかじめフォルダを指定しておいてそこに勝手に保存する(名前の重複があった場合は上書き)
　内部仕様
　　受信したデータは一時ファイルに書きだして元のファイル名と関連付けて管理しておく
*/

namespace Server
{
	public partial class Form5:Form
	{
		public Form5()
		{
			InitializeComponent();
		}

		private void Form5_FormClosing(object sender,FormClosingEventArgs e)
		{
			if(e.CloseReason==CloseReason.UserClosing){
				e.Cancel=true;
				Hide();
			}
		}
	}
}
