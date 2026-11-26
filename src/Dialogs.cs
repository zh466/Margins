using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Polya {
    class Sheet : Form {
        protected readonly int W,H;
        readonly Label subtitleLabel;
        public Sheet(string title,string subtitle,int w,int h) {
            W=w; H=h;
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(w,h); StartPosition=FormStartPosition.CenterParent;
            FormBorderStyle=FormBorderStyle.None; MaximizeBox=false; MinimizeBox=false; ShowInTaskbar=false;
            Text=title; BackColor=Art.White; Font=new Font("Segoe UI",10);DoubleBuffered=true;KeyPreview=true;
            var head=new Panel {BackColor=Art.Sidebar,Location=Point.Empty,Size=new Size(w,83),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};
            var name=new Label {Text=title,Location=new Point(20,12),Size=new Size(w-72,36),Font=new Font("Segoe UI",18,FontStyle.Bold),ForeColor=Art.Ink,AutoEllipsis=true};
            subtitleLabel=new Label {Text=subtitle,Location=new Point(22,51),Size=new Size(w-44,23),ForeColor=Art.Muted};
            head.Controls.Add(name);head.Controls.Add(subtitleLabel);Controls.Add(head);
            var close=new SheetButton("×",false) {Location=new Point(w-49,13),Size=new Size(34,32),AccessibleName="Закрыть",TabStop=false};
            close.Click+=(s,e)=>DialogResult=DialogResult.Cancel;
            head.Controls.Add(close);close.BringToFront();
            KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;e.SuppressKeyPress=true;}};
        }
        Action<DialogResult> boardResult;
        internal void OnBoard(Workspace owner,Action<DialogResult> result) {
            boardResult=result;
            TopLevel=false;
            var scroll=new Panel{AutoScroll=true,BackColor=Art.White,BorderStyle=BorderStyle.FixedSingle};
            scroll.Controls.Add(this);
            Location=Point.Empty;
            owner.ShowCanvasPanel(scroll,W+22);
            Show();
        }
        public new DialogResult DialogResult {
            get {return base.DialogResult;}
            set {
                if(boardResult==null) {base.DialogResult=value;return;}
                var done=boardResult;boardResult=null;
                var scroll=Parent;
                var owner=scroll==null?null:scroll.Parent as Workspace;
                if(owner!=null)owner.DetachCanvasPanel(scroll);
                try {done(value);}
                finally {if(scroll!=null)scroll.Dispose();}
            }
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData) {
            if(boardResult!=null&&keyData==Keys.Escape){DialogResult=DialogResult.Cancel;return true;}
            return base.ProcessCmdKey(ref message,keyData);
        }
        protected override void OnSizeChanged(EventArgs e) {
            base.OnSizeChanged(e);
            if(ClientSize.Width<24||ClientSize.Height<24) return;
            using(var path=new GraphicsPath()) {
                const int d=25;
                int w=ClientSize.Width,h=ClientSize.Height;
                path.AddArc(0,0,d,d,180,90);path.AddArc(w-d,0,d,d,270,90);
                path.AddArc(w-d,h-d,d,d,0,90);path.AddArc(0,h-d,d,d,90,90);path.CloseFigure();
                var old=Region;Region=new Region(path);if(old!=null)old.Dispose();
            }
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);Art.Prepare(e.Graphics);
            using(var p=Art.Pen(Art.Line,2)) e.Graphics.DrawRectangle(p,0,0,ClientSize.Width-1,ClientSize.Height-1);
        }
        public Label Caption(string text,int x,int y,int w) {return Put(new Label{Text=text,ForeColor=Art.Muted},x,y,w,23);}
        public T Put<T>(T control,int x,int y,int w,int h) where T:Control {
            control.Location=new Point(x,y);control.Size=new Size(w,h);Controls.Add(control);return control;
        }
        public TextBox Field(string text,int x,int y,int w) {
            var input=new TextBox{Text=text??"",ForeColor=Art.Ink,BackColor=Art.C("#FAF9FC"),BorderStyle=BorderStyle.None,
                Location=new Point(10,7),Width=w-20};
            InputFrame(input,x,y,w,31);
            return input;
        }
        public RichTextBox Body(string text,int x,int y,int w,int h,bool readOnly=false) {
            var body=new RichTextBox{Text=text??"",ForeColor=Art.Ink,BackColor=Art.C("#FAF9FC"),BorderStyle=BorderStyle.None,
                ReadOnly=readOnly,DetectUrls=false,WordWrap=true,AcceptsTab=false,ScrollBars=RichTextBoxScrollBars.Vertical,MaxLength=Int32.MaxValue};
            body.Location=new Point(10,8);body.Size=new Size(w-20,h-16);
            InputFrame(body,x,y,w,h);
            return body;
        }
        void InputFrame(Control input,int x,int y,int w,int h) {
            var frame=Put(new DiagramPane{BackColor=Art.White,TabStop=false},x,y,w,h);
            frame.Render=(g,r)=>Art.Round(g,new RectangleF(1,1,r.Width-2,r.Height-2),8,input.BackColor,input.Focused?Art.Purple:Art.Line);
            frame.Controls.Add(input);
            frame.MouseDown+=(s,e)=>input.Focus();
            input.GotFocus+=(s,e)=>frame.Invalidate();input.LostFocus+=(s,e)=>frame.Invalidate();
        }
        public Button Action(string title,int x,int y,int w,Action run,bool primary=false) {
            var btn=new SheetButton(title,primary);
            btn.Click+=(s,e)=>run();return Put(btn,x,y,w,34);
        }
        public void Footer(string text,Func<bool> done) {
            var cancel=Action("Отмена",W-243,H-54,92,()=>DialogResult=DialogResult.Cancel);
            cancel.DialogResult=DialogResult.Cancel;CancelButton=cancel;
            AcceptButton=Action(text,W-140,H-54,118,()=>{if(done()) DialogResult=DialogResult.OK;},true);
        }
        public bool Error(string message) {
            if(boardResult==null)Prompts.Alert(this,Text,message);
            else {subtitleLabel.Text=message;subtitleLabel.ForeColor=Art.C("#A32B52");}
            return false;
        }
    }
    sealed class SheetButton : Button {
        readonly bool primary;
        bool hovered;
        public float DrawingScale=1;
        public SheetButton(string label,bool primary) {
            this.primary=primary;Text=label;Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;
            BackColor=primary?Art.Purple:Art.White;ForeColor=primary?Art.White:Art.Ink;
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);
        }
        protected override void OnMouseEnter(EventArgs e) {base.OnMouseEnter(e);hovered=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e);hovered=false;Invalidate();}
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;g.Clear(Parent==null?Art.White:Parent.BackColor);Art.Prepare(g);
            var state=g.Save();
            g.ScaleTransform(DrawingScale,DrawingScale);
            float width=Width/DrawingScale,height=Height/DrawingScale;
            var fill=Enabled&&hovered?(primary?Art.C("#5236B7"):Art.Pale):BackColor;
            Art.Round(g,new RectangleF(1,1,width-2,height-2),9,fill,Focused?Art.Purple:primary?(Color?)null:Art.Line);
            Art.Label(g,Text,4,0,width-8,height,Text=="×"?21:13,Enabled?ForeColor:Art.Fade,true,StringAlignment.Center);
            g.Restore(state);
        }
    }
    sealed class DiagramPane : Panel {
        public Action<Graphics,RectangleF> Render;
        public DiagramPane() { DoubleBuffered=true; ResizeRedraw=true;BackColor=Art.C("#FAF9FD"); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);Art.Prepare(e.Graphics);
            if(Render!=null) Render(e.Graphics,new RectangleF(0,0,Width,Height));
        }
    }
    static class Dialogs {
        public static bool BoardName(IWin32Window owner,string title,string current,string[] subjects,out string name,out int subject,int initialSubject=3) {
            using(var sheet=new Sheet(title,subjects==null?"Переименуйте текущую доску":"Выберите предмет и название",433,subjects==null?214:275)) {
                sheet.Caption("Название",22,98,389);var field=sheet.Field(current,22,124,389);field.MaxLength=100;
                ComboBox selector=null;
                if(subjects!=null) {
                    sheet.Caption("Предмет",22,153,389);
                    selector=sheet.Put(new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList},22,178,389,28);
                    selector.Items.AddRange(subjects);selector.SelectedIndex=Math.Max(0,Math.Min(subjects.Length-1,initialSubject));
                }
                sheet.Footer("Готово",()=>!String.IsNullOrWhiteSpace(field.Text)||sheet.Error("Введите название доски."));
                sheet.ActiveControl=field;field.SelectAll();
                bool accepted=sheet.ShowDialog(owner)==DialogResult.OK;
                name=field.Text.Trim();subject=selector==null?0:selector.SelectedIndex; return accepted;
            }
        }
        public static void Edit(Workspace owner,Mark mark,Action<Mark> done) {
            bool link=mark.Kind=="link",heading=mark.Kind=="heading",note=mark.Kind=="note";
            var copy=mark.Copy();
            var sheet=new Sheet(link?"Ссылка":heading?"Заголовок":note?"Заметка":"Текст",
            link?"Страница откроется на доске после добавления":"Изменения появятся на холсте после сохранения",532,link||heading?278:474);
            sheet.Caption(link?"Название ссылки":"Заголовок",22,99,480); var title=sheet.Field(mark.Title,22,124,488); title.MaxLength=250;
            sheet.Caption(link?"Адрес HTTPS или HTTP":heading?"Подзаголовок":"Текст",22,161,480);
            var line=link||heading?sheet.Field(mark.Text,22,187,488):null;
            var body=line==null?sheet.Body(mark.Text,22,187,488,note?181:215):null;
            ComboBox colors=null;
            var names=new[]{"Песочная","Мятная","Лавандовая","Розовая","Белая"};
            var colorsHex=new[]{"#FFF1C3","#DDF4EB","#EEE7FF","#F9E6F0","#FFFFFF"};
            if(note) {
                colors=sheet.Put(new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList},22,379,181,26);
                colors.Items.AddRange(names);colors.SelectedIndex=Math.Max(0,Array.IndexOf(colorsHex,mark.Color));
            }
            sheet.Footer("Готово",()=>{
                copy.Title=title.Text.Trim();copy.Text=line==null?body.Text:line.Text.Trim();
                if(link) {
                    Uri uri;if(!Url(copy.Text,out uri)) return sheet.Error("Введите полный веб-адрес, например https://example.org.");
                    if(String.IsNullOrEmpty(copy.Title)) copy.Title=uri.Host;
                }
                if(colors!=null) copy.Color=colorsHex[colors.SelectedIndex];
                return !String.IsNullOrWhiteSpace(copy.Title+copy.Text)||sheet.Error("Введите текст или заголовок.");
            });
            sheet.ActiveControl=link?(Control)line:body??(Control)title;
            sheet.OnBoard(owner,result=>done(result==DialogResult.OK?copy:null));
        }
        static bool Number(string text,out double result) {
            return Double.TryParse(text.Trim().Replace(',','.').Replace("−","-"),NumberStyles.Float,CultureInfo.InvariantCulture,out result)&&
                !Double.IsNaN(result)&&!Double.IsInfinity(result);
        }
        public static void Graph(Workspace owner,Mark mark,Action<Mark> done) {
            var copy=mark.Copy();
            var sheet=new Sheet("График функции","Введите sin(x), x^2 или a*x^2. Углы в радианах.",565,580);
            var redraw=new Timer{Interval=180};
            sheet.Disposed+=(s,e)=>redraw.Dispose();
            sheet.Caption("Функция y = f(x)",22,96,290);var input=sheet.Field(copy.Text,22,123,521);input.MaxLength=512;
            sheet.Caption("x от",22,157,100);sheet.Caption("x до",158,157,100);
            var from=sheet.Field(copy.From.ToString("G",CultureInfo.InvariantCulture),22,181,120);
            var to=sheet.Field(copy.To.ToString("G",CultureInfo.InvariantCulture),158,181,120);
            var parameter=sheet.Caption("",22,211,521);parameter.ForeColor=Art.Purple;
            var dial=sheet.Put(new TrackBar{Minimum=-100,Maximum=100,TickFrequency=25,Value=(int)Math.Round(Math.Max(-10,Math.Min(10,mark.Parameter??1))*10)},19,237,522,42);
            var error=sheet.Caption("",22,281,521);error.ForeColor=Art.C("#A32B52");
            var plot=sheet.Put(new DiagramPane(),22,309,521,198);
            Mark preview=null;
            plot.Render=(g,r)=>{if(preview!=null) GraphArt.Plot(g,r,preview,Art.Purple);};
            Func<bool> validate=()=>{
                double a,b;
                if(!Number(from.Text,out a)||!Number(to.Text,out b)||a>=b||Math.Abs(a)>1e9||Math.Abs(b)>1e9) {
                    error.Text="Введите диапазон от меньшего к большему, в пределах ±10⁹."; return false;
                }
                parameter.Text="Параметр a   "+(dial.Value/10.0).ToString("0.0",CultureInfo.CurrentCulture)+"   ·   используйте a в формуле";
                try {Formula.Compile(input.Text,parameter:dial.Value/10.0);}
                catch(FormatException ex) {error.Text=ex.Message;return false;}
                error.Text=""; copy.Text=input.Text.Trim();copy.From=a;copy.To=b;copy.Parameter=dial.Value/10.0;
                preview=copy.Copy();plot.Invalidate(); return true;
            };
            EventHandler refresh=(s,e)=>{redraw.Stop();redraw.Start();};
            input.TextChanged+=refresh;from.TextChanged+=refresh;to.TextChanged+=refresh;
            dial.ValueChanged+=(s,e)=>{if(!validate()){preview=null;plot.Invalidate();}};
            redraw.Tick+=(s,e)=>{redraw.Stop();if(!validate()) {preview=null;plot.Invalidate();}};
            bool isNew=String.IsNullOrWhiteSpace(mark.Title)||mark.Title=="График функции";
            sheet.Footer(isNew?"На доску":"Готово",validate);validate();
            sheet.ActiveControl=input;input.SelectAll();
            sheet.OnBoard(owner,result=>done(result==DialogResult.OK?copy:null));
        }
        public static void Trig(Workspace owner,Mark mark,Action<Mark> done) {
            var copy=mark.Copy();
            var sheet=new Sheet("Тригонометрическая окружность","Передвигайте точку на окружности или меняйте угол",520,558);
            var pane=sheet.Put(new DiagramPane(),22,101,476,264);
            var angle=sheet.Put(new NumericUpDown { Minimum=0,Maximum=360,DecimalPlaces=1,Increment=1,
                Value=(decimal)((mark.Angle%360+360)%360) },381,379,117,28);
            var slider=sheet.Put(new TrackBar{Minimum=0,Maximum=360,TickFrequency=45,SmallChange=1,LargeChange=15},20,372,350,43);
            var values=sheet.Caption("",25,428,470);
            values.Font=new Font("Consolas",11,FontStyle.Bold);
            values.ForeColor=Art.Purple;
            Action refresh=()=>{
                copy.Angle=(double)angle.Value;
                double rad=copy.Angle*Math.PI/180;
                values.Text=String.Format(CultureInfo.InvariantCulture,"sin α = {0:0.000}     cos α = {1:0.000}     α = {2:0.000} рад",Math.Sin(rad),Math.Cos(rad),rad);
                pane.Invalidate();
            };
            pane.Render=(g,r)=>GraphArt.Circle(g,r,copy.Angle);
            angle.ValueChanged+=(s,e)=>{slider.Value=(int)Math.Round(angle.Value);refresh();};
            slider.Scroll+=(s,e)=>angle.Value=slider.Value;
            MouseEventHandler track=(s,e)=>{
                if(e.Button!=MouseButtons.Left) return;
                double a=Math.Atan2(pane.Height/2.0-e.Y,e.X-pane.Width/2.0)*180/Math.PI;
                angle.Value=(decimal)Math.Round((a+360)%360,1);
            };
            pane.MouseDown+=track; pane.MouseMove+=track; pane.Cursor=Cursors.Cross;
            slider.Value=(int)Math.Round(angle.Value);refresh();
            sheet.Footer("На доску",()=>true);
            sheet.OnBoard(owner,result=>done(result==DialogResult.OK?copy:null));
        }
        static readonly string[][] Physics={
            new[]{"Ускорение свободного падения","Стандартное ускорение gₙ = 9,80665 м/с².\n\nВ школьных задачах у поверхности Земли обычно используют g ≈ 9,81 м/с². Для грубой оценки g ≈ 10 м/с²."},
            new[]{"Скорость света","Скорость света в вакууме c = 299 792 458 м/с (точно).\n\nДля оценки c ≈ 3,00 × 10⁸ м/с."},
            new[]{"Гравитационная постоянная","G ≈ 6,67430 × 10⁻¹¹ Н·м²/кг².\n\nЗакон всемирного тяготения: F = Gm₁m₂/r²."},
            new[]{"Элементарный заряд","e = 1,602176634 × 10⁻¹⁹ Кл (точно).\n\nЗаряд электрона qₑ = −e."},
            new[]{"Постоянная Планка","h = 6,62607015 × 10⁻³⁴ Дж·с (точно).\n\nЭнергия фотона: E = hν."},
            new[]{"Постоянная Больцмана","k = 1,380649 × 10⁻²³ Дж/К (точно).\n\nСвязывает температуру с энергией на уровне частиц."},
            new[]{"Постоянная Авогадро","Nₐ = 6,02214076 × 10²³ моль⁻¹ (точно).\n\nЧисло частиц в n молях: N = nNₐ."},
            new[]{"Единицы СИ","Сила — ньютон: 1 Н = 1 кг·м/с².\nРабота — джоуль: 1 Дж = 1 Н·м.\nМощность — ватт: 1 Вт = 1 Дж/с.\nДавление — паскаль: 1 Па = 1 Н/м²."}
        };
        static readonly string[][] Russian={
            new[]{"Приставки на З и С","Перед звонким согласным пишем з, перед глухим — с.\n\nбезвредный → бесшумный\nразбить → расписать\n\nПриставка с- пишется одинаково: сдать, спеть."},
            new[]{"ТЬСЯ и ТСЯ","Что делать? Что сделать? — учиться, улыбнуться.\nЧто делает? Что сделает? — учится, улыбнётся.\n\nВ неопределённой форме перед -ся сохраняется ь."},
            new[]{"Проверяемые гласные","Подберите форму слова или однокоренное слово, где нужная гласная под ударением.\n\nвода́ → во́ды\nлеса́ → лес\n\nУ корней с чередованием свои правила."},
            new[]{"Вводные слова","Вводные слова выражают отношение говорящего, источник сообщения или порядок мыслей. Обычно их выделяют запятыми.\n\nКажется, дождь закончился.\n\nСравните: Этот рассказ кажется знакомым."},
            new[]{"Запятые при перечислении","Однородные члены без союза разделяются запятой. Перед одиночным соединительным союзом «и» запятая между однородными членами обычно не ставится.\n\nНа столе лежали ручка, карандаш и линейка."}
        };
        static readonly string[][] MathRef={
            new[]{"Основное тождество","sin² α + cos² α = 1\n\nДля точки единичной окружности (cos α, sin α) оно следует из x² + y² = 1."},
            new[]{"Формулы сложения","sin(α ± β) = sin α cos β ± cos α sin β\n\ncos(α + β) = cos α cos β − sin α sin β\ncos(α − β) = cos α cos β + sin α sin β"},
            new[]{"Градусы и радианы","180° = π рад\n90° = π/2 рад\n60° = π/3 рад\n45° = π/4 рад\n30° = π/6 рад"},
            new[]{"Квадратное уравнение","ax² + bx + c = 0, a ≠ 0\nD = b² − 4ac\n\nПри D > 0: x₁,₂ = (−b ± √D)/(2a).\nПри D = 0: x = −b/(2a).\nПри D < 0 действительных корней нет."},
            new[]{"Свойства логарифмов","При a > 0, a ≠ 1 и x, y > 0:\n\nlogₐ(xy) = logₐ x + logₐ y\nlogₐ(x/y) = logₐ x − logₐ y\nlogₐ(xʳ) = r logₐ x"}
        };
        public static void Reference(Workspace owner,string subject,Action<string[]> done) {
            var rows=subject=="physics"?Physics:subject=="russian"?Russian:MathRef;
            var sheet=new Sheet(subject=="physics"?"Физические постоянные":subject=="russian"?"Шпаргалки":"Справочник",Seed.Subject(subject),659,446);
            var list=sheet.Put(new ListBox{BorderStyle=BorderStyle.None,IntegralHeight=false,ForeColor=Art.Ink,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=36},22,104,232,271);
            var body=sheet.Body("",268,104,369,271,true);
            body.BackColor=Art.C("#FAF9FD");
            list.DrawItem+=(s,e)=>{
                if(e.Index<0) return;
                Art.Prepare(e.Graphics);
                bool selected=(e.State&DrawItemState.Selected)!=0;
                var r=new RectangleF(e.Bounds.X+1,e.Bounds.Y+2,e.Bounds.Width-3,e.Bounds.Height-5);
                Art.Round(e.Graphics,r,8,selected?Art.Pale:Art.C("#F7F6FB"));
                Art.Label(e.Graphics,rows[e.Index][0],r.X+10,r.Y,r.Width-18,r.Height,13,selected?Art.Purple:Art.Ink,selected);
            };
            list.Items.AddRange(rows.Select(row=>row[0]).ToArray());
            list.SelectedIndexChanged+=(s,e)=>{if(list.SelectedIndex>=0) body.Text=rows[list.SelectedIndex][0]+"\n\n"+rows[list.SelectedIndex][1];};
            list.SelectedIndex=0; sheet.Footer("На доску",()=>list.SelectedIndex>=0);
            sheet.OnBoard(owner,result=>done(result==DialogResult.OK?rows[list.SelectedIndex]:null));
        }
        public static void Help(IWin32Window owner) {
            using(var sheet=new Sheet("Горячие клавиши","Рисование, навигация и файлы",500,478)) {
                sheet.Body("V — выбор                 H / Пробел — рука\nP — перо                   R — прямоугольник\nO — эллипс               A — стрелка         L — линия\nT — текст                 N — заметка          K — ссылка\nE — ластик объектов    Shift+E — пиксельный ластик\n1–8 — инструменты от выделения до текста     9 — заметка\nDel — удалить\nD — ромб                  Q — закрепить инструмент\n\nCtrl+Z — отмена        Ctrl+Y — повторить\nCtrl+D — дублировать выбранный объект\nКолесо — масштаб    0 — показать всю доску\nShift при рисовании — круг, квадрат или шаг 45°\nДвойной щелчок — редактирование или просмотр ссылки\n\nCtrl+S — сохранить      Ctrl+Shift+S — сохранить как\nCtrl+O — открыть         Ctrl+N — новая тетрадь\nF2 — переименовать доску\nПКМ по холсту — команды доски",22,103,456,301,true).BorderStyle=BorderStyle.None;
                sheet.AcceptButton=sheet.Action("Закрыть",360,424,118,()=>sheet.Close(),true);
                sheet.ShowDialog(owner);
            }
        }
        internal static bool Url(string value,out Uri uri) {
            return Uri.TryCreate(value,UriKind.Absolute,out uri)&&(uri.Scheme=="https"||uri.Scheme=="http")&&
                String.IsNullOrEmpty(uri.UserInfo)&&!String.IsNullOrEmpty(uri.Host);
        }
        public static void OpenUrl(IWin32Window owner,string value) {
            Uri uri;
            if(!Url(value,out uri)) {Prompts.Alert(owner,"Некорректная ссылка","Введите полный адрес HTTPS или HTTP.");return;}
            try {Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}
            catch(Exception ex) {Prompts.Alert(owner,"Не удалось открыть браузер",ex.Message);}
        }
        public static void Preview(IWin32Window owner,Mark link) {
            Uri uri;if(!Url(link.Text,out uri)) return;
            using(var sheet=new Sheet("Просмотр ссылки",uri.Host,622,460)) {
                var address=sheet.Field(uri.AbsoluteUri,22,102,578);address.ReadOnly=true;
                var body=sheet.Body(link.Title+"\n\nЗагрузка страницы…",22,144,578,246,true);
                sheet.Action("Скопировать адрес",22,407,160,()=>Clipboard.SetText(uri.AbsoluteUri));
                sheet.Action("Открыть в браузере",420,407,180,()=>OpenUrl(sheet,uri.AbsoluteUri),true);
                sheet.Shown+=async (s,e)=>{
                    string preview;
                    try {preview=await FetchPreview(uri);}
                    catch(Exception) {preview=link.Title+"\n\nПредпросмотр недоступен. Страницу можно открыть в браузере.";}
                    if(!sheet.IsDisposed) body.Text=preview;
                };
                sheet.ShowDialog(owner);
            }
        }
        static async Task<string> FetchPreview(Uri uri) {
            using(var handler=new HttpClientHandler{AutomaticDecompression=DecompressionMethods.Deflate|DecompressionMethods.GZip,UseCookies=false})
            using(var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(7)})
            using(var timeout=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8))) {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Polya/1.0 (link preview)");
                using(var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
                    response.EnsureSuccessStatusCode();
                    string media=response.Content.Headers.ContentType==null?"":response.Content.Headers.ContentType.MediaType;
                    if(media!="text/html"&&media!="application/xhtml+xml"&&media!="text/plain") return "Документ · "+media+"\n\nОткройте файл в браузере для просмотра.";
                    var bytes=new byte[384*1024];int total=0;
                    using(var data=await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) {
                        while(total<bytes.Length) {
                            int read=await data.ReadAsync(bytes,total,bytes.Length-total,timeout.Token).ConfigureAwait(false);
                            if(read==0) break;total+=read;
                        }
                    }
                    var encoding=Encoding.UTF8;
                    var charset=response.Content.Headers.ContentType.CharSet;
                    if(!String.IsNullOrEmpty(charset)) try {encoding=Encoding.GetEncoding(charset.Trim('"'));} catch(ArgumentException) {}
                    var html=encoding.GetString(bytes,0,total);
                    if(media=="text/plain") return html.Substring(0,Math.Min(6200,html.Length));
                    return ExtractPreview(html,uri.Host);
                }
            }
        }
        static readonly RegexOptions HtmlOptions=RegexOptions.IgnoreCase|RegexOptions.Singleline|RegexOptions.CultureInvariant;
        internal static string ExtractPreview(string html,string host) {
            var budget=TimeSpan.FromMilliseconds(500);
            html=Regex.Replace(html,@"<(script|style|noscript|svg|header|footer|nav)\b[^>]*>.*?</\1\s*>"," ",HtmlOptions,budget);
            var title=Plain(Regex.Match(html,@"<title\b[^>]*>(.*?)</title\s*>",HtmlOptions,budget).Groups[1].Value);
            var desc=Regex.Matches(html,@"<meta\b[^>]*>",HtmlOptions,budget).Cast<Match>().Select(m=>m.Value).FirstOrDefault(m=>
                Regex.IsMatch(m,@"\b(?:name|property)\s*=\s*[""'](?:og:)?description[""']",HtmlOptions,budget));
            var description=desc==null?"":Plain(Regex.Match(desc,@"\bcontent\s*=\s*(?:""([^""]*)""|'([^']*)')",HtmlOptions,budget)
                .Groups.Cast<Group>().Skip(1).Select(m=>m.Value).FirstOrDefault(m=>m.Length>0)??"");
            var excerpt=new List<string>();
            foreach(Match match in Regex.Matches(html,@"<p\b[^>]*>(.*?)</p\s*>",HtmlOptions,budget)) {
                var paragraph=Plain(match.Groups[1].Value);
                if(paragraph.Length>=45&&!excerpt.Contains(paragraph)&&paragraph!=description) excerpt.Add(paragraph);
                if(excerpt.Count>=8||excerpt.Sum(p=>p.Length)>5600) break;
            }
            var result=(title.Length==0?host:title)+"\n\n";
            if(description.Length>0) result+=description+"\n\n";
            result+=excerpt.Count>0?String.Join("\n\n",excerpt):description.Length==0?"На странице нет доступного текстового описания.":"";
            return result.Substring(0,Math.Min(result.Length,6200)).TrimEnd();
        }
        static string Plain(string text) {
            var time=TimeSpan.FromMilliseconds(350);
            return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text,"<[^>]+>"," ",HtmlOptions,time)),@"\s+"," ",HtmlOptions,time).Trim();
        }
    }
}
