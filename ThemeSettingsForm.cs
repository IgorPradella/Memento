using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Memento
{
    internal sealed class ThemeSettingsForm : Form
    {
        private readonly RadioButton[] defaults = new RadioButton[Palette.All.Length];
        private readonly TextBox[] names = new TextBox[Palette.All.Length];
        public UserSettings Result { get; private set; }
        public ThemeSettingsForm(UserSettings current)
        {
            Text = "Memento · temas e assuntos"; Icon = Product.LoadIcon();
            ClientSize = new Size(560, 484); StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(249, 248, 244);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Controls.Add(new Label { Text = "Cores que organizam suas ideias", Font = new Font("Segoe UI", 16, FontStyle.Bold), Location = new Point(24, 18), AutoSize = true });
            Controls.Add(new Label { Text = "Renomeie cada tema como um assunto e escolha o padrão\npara todas as novas notas e títulos.", Location = new Point(25, 60), Size = new Size(510, 48) });
            Controls.Add(new Label { Text = "PADRÃO", Location = new Point(25, 118), Size = new Size(75, 24), Font = new Font("Segoe UI", 8, FontStyle.Bold) });
            Controls.Add(new Label { Text = "NOME DO TEMA / ASSUNTO", Location = new Point(143, 118), Size = new Size(350, 24), Font = new Font("Segoe UI", 8, FontStyle.Bold) });
            for (int i = 0; i < Palette.All.Length; i++)
            {
                Palette p = Palette.All[i]; int y = 148 + i * 38;
                defaults[i] = new RadioButton { Location = new Point(45, y + 4), Size = new Size(24, 24), Checked = current.DefaultTheme == p.Key, AccessibleName = "Usar " + p.Name + " como padrão" };
                Label swatch = new Label { Location = new Point(93, y), Size = new Size(32, 28), BackColor = p.Background, BorderStyle = BorderStyle.FixedSingle, AccessibleName = p.Name };
                names[i] = new TextBox { Location = new Point(143, y), Size = new Size(388, 28), MaxLength = 40, Text = current.ThemeName(p.Key), AccessibleName = "Nome do tema " + p.Name };
                Controls.Add(defaults[i]); Controls.Add(swatch); Controls.Add(names[i]);
            }
            Button cancel = new Button { Text = "Cancelar", Location = new Point(323, 433), Size = new Size(94, 32), DialogResult = DialogResult.Cancel };
            Button save = new Button { Text = "Salvar", Location = new Point(431, 433), Size = new Size(100, 32), BackColor = Color.FromArgb(39, 55, 43), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            save.Click += delegate
            {
                Result = new UserSettings();
                for (int i = 0; i < Palette.All.Length; i++)
                { Result.ThemeNames[Palette.All[i].Key] = names[i].Text; if (defaults[i].Checked) Result.DefaultTheme = Palette.All[i].Key; }
                Result.Normalize(); DialogResult = DialogResult.OK; Close();
            };
            Controls.Add(cancel); Controls.Add(save); AcceptButton = save; CancelButton = cancel;
        }
    }
}
