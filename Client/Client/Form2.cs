﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Client
{
	public partial class Form2:Form
	{
		public Form2()
		{
			InitializeComponent();
		}

		private void Form2_FormClosing(object sender,FormClosingEventArgs e)
		{
			if(e.CloseReason==CloseReason.UserClosing){
				e.Cancel=true;
				Hide();
			}
		}
	}
}
