using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Polya {
    sealed class CalculatorWindow : Sheet {
        readonly TextBox input,result;
        readonly ComboBox model,units,operation;
        readonly DiagramPane body;
        readonly Color lcd=Art.C("#D5DDC0");
        double ans,memory,variable;
        bool shifted,fraction;
        readonly System.Collections.Generic.List<string> history=new System.Collections.Generic.List<string>();
        int historyIndex;
        DeviceKey shiftKey;
        readonly System.Collections.Generic.Dictionary<DeviceKey,string> alternate=new System.Collections.Generic.Dictionary<DeviceKey,string>();

        public CalculatorWindow():base("Калькулятор","Выберите раскладку и режим вычислений",405,708) {
            model=Put(new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList},22,97,208,26);
            model.Items.AddRange(new[]{"Научный","Расширенный"});
            units=Put(new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList},237,97,154,26);
            units.Items.AddRange(new[]{"Радианы","Градусы"});units.SelectedIndex=0;
            body=Put(new DiagramPane{BackColor=Art.White},17,134,370,553);
            body.Render=(g,r)=>{
                var shell=new RectangleF(4,2,r.Width-8,r.Height-6);
                using(var path=Art.Path(shell,26))
                using(var gradient=new LinearGradientBrush(shell,Art.C("#3B4252"),Art.C("#161C27"),80)) g.FillPath(gradient,path);
                Art.Round(g,new RectangleF(12,10,r.Width-24,r.Height-22),20,Color.Transparent,Art.C("#606A7B"));
                Art.Label(g,"MARGINS",23,17,155,21,13,Art.White,true);
                Art.Label(g,model.SelectedIndex==0?"S1":"S2",290,17,49,21,12,Art.C("#A9B5C9"),true,StringAlignment.Far);
                Art.Round(g,new RectangleF(22,48,r.Width-44,93),10,lcd,Art.C("#1B231F"));
            };
            input=new TextBox{BackColor=lcd,ForeColor=Art.C("#15211A"),BorderStyle=BorderStyle.None,Font=new Font("Consolas",15),MaxLength=512};
            result=new TextBox{BackColor=lcd,ForeColor=Art.C("#15211A"),BorderStyle=BorderStyle.None,Font=new Font("Consolas",17),ReadOnly=true,TextAlign=HorizontalAlignment.Right};
            Place(input,33,59,302,25);Place(result,33,92,302,30);
            operation=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
            operation.Items.AddRange(new[]{"Выражение","Среднее · числа через ;","σ · числа через ;","Решить f(x)=0 · f(x); a; b"});
            operation.SelectedIndex=0;Place(operation,22,152,229,23);
            KeyButton("Копия",253,149,94,28,()=>{if(result.Text.Length>0) Clipboard.SetText(result.Text);});
            model.SelectedIndexChanged+=(s,e)=>{
                if(model.SelectedIndex==0)operation.SelectedIndex=0;
                operation.Enabled=model.SelectedIndex==1;body.Invalidate();
            };
            model.SelectedIndex=0;
            shiftKey=(DeviceKey)KeyButton("2nd",22,189,51,32,()=>SetShift(!shifted));
            KeyButton("◀",80,189,50,32,()=>Caret(-1));
            KeyButton("▶",136,189,50,32,()=>Caret(1));
            KeyButton("↑",192,189,50,32,()=>History(-1));
            KeyButton("↓",248,189,50,32,()=>History(1));
            KeyButton("a/b",304,189,44,32,ToggleFraction);
            string[] functionKeys={"sin","cos","tan","ln","log","√","x²","xʸ","1/x","|x|","n!","π","MC","MR","M+","M−","STO","x"};
            string[] secondKeys={"asin","acos","atan","eˣ","10ˣ","∛","x³","ⁿ√","%","round","nCr","e","sinh","cosh","tanh","mod","nPr",";"};
            for(int i=0;i<functionKeys.Length;i++) {
                string regular=functionKeys[i],second=secondKeys[i];
                int x=22+i%6*56,y=227+i/6*35;
                var button=(DeviceKey)KeyButton(regular,x,y,51,31,()=>{Key(shifted?second:regular);if(shifted)SetShift(false);});
                alternate.Add(button,second);button.PrimaryLabel=regular;
            }
            string[,] keypad={{"7","8","9","⌫","AC"},{"4","5","6","×","÷"},{"1","2","3","+","−"},{"0",".","(",")","="}};
            for(int y=0;y<keypad.GetLength(0);y++)for(int x=0;x<keypad.GetLength(1);x++) {
                string label=keypad[y,x];
                var button=KeyButton(label,22+x*67,335+y*47,61,42,()=>Key(label));
                if(label=="=")AcceptButton=button;
            }
            input.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){Calculate();e.SuppressKeyPress=true;}};
        }
        void SetShift(bool enabled) {
            shifted=enabled;shiftKey.Text=enabled?"2nd ●":"2nd";shiftKey.Invalidate();
            foreach(var key in alternate){key.Key.Text=enabled?key.Value:key.Key.PrimaryLabel;key.Key.Invalidate();}
        }
        void Caret(int direction) {
            input.SelectionStart=Math.Max(0,Math.Min(input.TextLength,input.SelectionStart+direction));
            input.SelectionLength=0;input.Focus();
        }
        void History(int direction) {
            if(history.Count==0)return;
            historyIndex=Math.Max(0,Math.Min(history.Count,historyIndex+direction));
            input.Text=historyIndex==history.Count?"":history[historyIndex];
            input.SelectionStart=input.TextLength;input.Focus();
        }
        void ToggleFraction() {
            fraction=!fraction;
            if(result.Text.Length>0)FormatResult();
        }
        void FormatResult() {
            if(fraction&&Math.Abs(ans)<1e12) {
                var value=Math.Abs(ans);
                for(int denominator=1;denominator<=9999;denominator++) {
                    double numerator=Math.Round(value*denominator);
                    if(Math.Abs(numerator/denominator-value)>1e-12)continue;
                    if(denominator>1) {
                        result.Text=(ans<0?"−":"")+numerator.ToString("0",CultureInfo.InvariantCulture)+"/"+
                            denominator.ToString(CultureInfo.InvariantCulture);
                        return;
                    }
                    break;
                }
            }
            result.Text=ans.ToString("G12",CultureInfo.InvariantCulture);
        }
        void Place(Control c,int x,int y,int w,int h) {
            c.Bounds=new Rectangle(x,y,w,h);body.Controls.Add(c);
        }
        Button KeyButton(string label,int x,int y,int w,int h,Action action) {
            var button=new DeviceKey(label);
            button.Click+=(s,e)=>action();Place(button,x,y,w,h);return button;
        }
        sealed class DeviceKey : Button {
            bool hover,pressed;
            public string PrimaryLabel;
            public DeviceKey(string label) {
                Text=label;AccessibleName=label;TabStop=false;Cursor=Cursors.Hand;
                SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);
                BackColor=Color.Transparent;
            }
            protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Invalidate();}
            protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=pressed=false;Invalidate();}
            protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);pressed=true;Invalidate();}
            protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);pressed=false;Invalidate();}
            protected override void OnPaint(PaintEventArgs e) {
                var g=e.Graphics;Art.Prepare(g);
                bool number=Char.IsDigit(Text[0]),accent=Text=="=",clear=Text=="AC";
                var r=new RectangleF(1,pressed?3:1,Width-2,Height-6);
                Art.Round(g,new RectangleF(1,4,Width-2,Height-5),9,Art.C("#111721"));
                var top=accent?Art.C("#A990EF"):clear?Art.C("#EAB2A4"):number?Art.C("#EDF0F6"):Art.C("#475365");
                if(hover)top=ControlPaint.Light(top,.18f);
                Art.Round(g,r,9,top);
                Art.Label(g,Text,0,r.Y,Width,r.Height,number?17:13,number||accent||clear?Art.C("#182238"):Art.White,true,StringAlignment.Center);
            }
        }
        void Key(string key) {
            switch(key) {
                case "=":Calculate();return;
                case "AC":input.Clear();result.Clear();input.Focus();return;
                case "MC":memory=0;return;
                case "STO":variable=ans;return;
                case "MR":key=memory.ToString("G15",CultureInfo.InvariantCulture);break;
                case "M+":memory+=ans;return;
                case "M−":memory-=ans;return;
                case "⌫":
                    if(input.SelectionLength>0)input.SelectedText="";
                    else if(input.SelectionStart>0){input.SelectionStart--;input.SelectionLength=1;input.SelectedText="";}
                    input.Focus();return;
                case "sin":case "cos":case "tan":case "asin":case "acos":case "atan":case "ln":case "log":case "sinh":case "cosh":case "tanh":
                case "round":case "nCr":case "nPr":case "mod":key+="(";break;
                case "eˣ":key="exp(";break;
                case "10ˣ":key="10^(";break;
                case "x²":key="^2";break;
                case "x³":key="^3";break;
                case "1/x":key="1/(";break;
                case "|x|":key="abs(";break;
                case "%":key="pct(";break;
                case "∛":key="cbrt(";break;
                case "ⁿ√":key="root(";break;
                case "√":key="sqrt(";break;
                case "xʸ":key="^";break;
                case "n!":key="!";break;
            }
            input.SelectedText=key;input.Focus();
        }
        double Evaluate(string expression) {return Formula.Compile(expression,units.SelectedIndex==1,ans)(variable);}
        double Solve(string[] args) {
            if(args.Length!=3)throw new FormatException("Формат: f(x); a; b");
            var f=Formula.Compile(args[0],units.SelectedIndex==1,ans);
            double a=Evaluate(args[1]),b=Evaluate(args[2]),fa=f(a),fb=f(b);
            if(fa==0)return a;if(fb==0)return b;
            if(!(a<b)||!Finite(fa)||!Finite(fb)||Math.Sign(fa)==Math.Sign(fb))
                throw new FormatException("Нужен интервал со сменой знака");
            for(int i=0;i<70;i++) {
                double x=a/2+b/2,fx=f(x);
                if(!Finite(fx))throw new FormatException("Разрыв функции");
                if(Math.Abs(fx)<1e-11)return x;
                if(Math.Sign(fx)==Math.Sign(fa))a=x;else b=x;
            }
            throw new FormatException("Корень не найден");
        }
        static bool Finite(double value) {return !Double.IsNaN(value)&&!Double.IsInfinity(value);}
        void Calculate() {
            try {
                double value;
                if(operation.SelectedIndex==0)value=Evaluate(input.Text);
                else if(operation.SelectedIndex==3)value=Solve(input.Text.Split(';'));
                else {
                    var items=input.Text.Split(';').Select(Evaluate).ToArray();
                    var mean=items.Average();
                    value=operation.SelectedIndex==1?mean:Math.Sqrt(items.Average(v=>(v-mean)*(v-mean)));
                }
                if(!Finite(value))throw new FormatException("Вне области определения");
                ans=value;FormatResult();
                if(history.Count==0||history[history.Count-1]!=input.Text) {
                    if(history.Count>=50)history.RemoveAt(0);
                    history.Add(input.Text);
                }
                historyIndex=history.Count;
            } catch(FormatException ex) {result.Text=ex.Message;}
            input.Focus();
        }
    }
}
