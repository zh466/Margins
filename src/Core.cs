using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace Polya {
    public class Mark {
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public string Color { get; set; }
        public string Fill { get; set; }
        public string Dash { get; set; }
        public int? Opacity { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float W { get; set; }
        public float H { get; set; }
        public float Size { get; set; }
        public float[] Points { get; set; }
        public double From { get; set; }
        public double To { get; set; }
        public double Angle { get; set; }
        public string Answer { get; set; }
        public bool Revealed { get; set; }
        public double? Parameter { get; set; }
        public float[] DiagramPoints { get; set; }
        public float Rotation { get; set; }
        public bool? Labels { get; set; }
        public bool Measurements { get; set; }
        public CircuitPart[] Circuit { get; set; }
        public double? Voltage { get; set; }
        public bool RemoveWhite { get; set; }
        [ScriptIgnore] public RectangleF Bounds { get { return new RectangleF(X,Y,W,H); } }
        public Mark Copy() { return (Mark)MemberwiseClone(); }
        public static Mark Make(string kind, float x, float y, float w, float h, string title = "", string body = "", string color = "") {
            return new Mark { Kind=kind, X=x, Y=y, W=w, H=h, Title=title, Text=body, Color=color, Size=2.4f, From=-6, To=6 };
        }
    }
    public class Board {
        public string Name { get; set; }
        public string Subject { get; set; }
        public List<Mark> Marks { get; set; }
        public float ViewX { get; set; }
        public float ViewY { get; set; }
        public float Zoom { get; set; }
        public string Background { get; set; }
        public string Pattern { get; set; }
        public bool? Straighten { get; set; }
        public static Board Make(string name, string subject) { return new Board { Name=name, Subject=subject, Marks=new List<Mark>(), Zoom=1 }; }
    }
    public class Notebook {
        public int Version { get; set; }
        public List<Board> Boards { get; set; }
        public int Active { get; set; }
        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength=Int32.MaxValue }; }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool MoveFileEx(string source,string destination,int flags);
        public static Notebook Read(string path) {
            var doc=Serializer().Deserialize<Notebook>(File.ReadAllText(path));
            if(doc==null || doc.Version!=1 || doc.Boards==null || doc.Boards.Count==0) throw new FormatException("Этот файл не является доской Поля версии 1.");
            var kinds=new HashSet<string>{"heading","text","note","link","graph","trig","ink","ellipse","rect","diamond","line","arrow","diagram","frame","task","image"};
            foreach(var board in doc.Boards) {
                if(board==null || board.Marks==null || String.IsNullOrWhiteSpace(board.Name) || !Finite(board.ViewX) || !Finite(board.ViewY)) throw new FormatException("Не удалось прочитать данные доски.");
                if(board.Zoom<.15f || board.Zoom>4 || !Finite(board.Zoom)) board.Zoom=1;
                board.Subject=Seed.Subjects.Contains(board.Subject)?board.Subject:"other";
                foreach(var mark in board.Marks) {
                    if(mark==null || !kinds.Contains(mark.Kind) || !Finite(mark.X) || !Finite(mark.Y) || !Finite(mark.W) || !Finite(mark.H) || !Finite(mark.Size) ||
                        mark.W<0 || mark.H<0 || (mark.Points!=null && (mark.Points.Length%2!=0 || mark.Points.Any(v=>!Finite(v)))))
                        throw new FormatException("В файле есть некорректный объект.");
                    if(mark.Kind=="graph"&&(!(mark.From<mark.To)||Math.Abs(mark.From)>1e9||Math.Abs(mark.To)>1e9))
                        throw new FormatException("В файле есть график с некорректным диапазоном.");
                    if(mark.Kind=="trig"&&(Double.IsNaN(mark.Angle)||Double.IsInfinity(mark.Angle)))
                        throw new FormatException("В файле есть окружность с некорректным углом.");
                    if(mark.Parameter.HasValue&&(Double.IsNaN(mark.Parameter.Value)||Math.Abs(mark.Parameter.Value)>1000000))
                        throw new FormatException("В файле есть график с некорректным параметром.");
                    if(mark.Kind=="diagram")DiagramEdit.Validate(mark);
                    mark.Size=Math.Max(1,Math.Min(30,mark.Size));
                    if(mark.Opacity.HasValue) mark.Opacity=Math.Max(10,Math.Min(100,mark.Opacity.Value));
                    mark.Text=mark.Text??"";
                    mark.Title=mark.Title??"";
                }
            }
            doc.Active=Math.Max(0,Math.Min(doc.Active,doc.Boards.Count-1));
            return doc;
        }
        static bool Finite(float value) { return !Single.IsNaN(value)&&!Single.IsInfinity(value)&&Math.Abs(value)<=10000000; }
        public void Write(string path) {
            string temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), ".polya-"+Guid.NewGuid().ToString("N")+".tmp");
            try {
                File.WriteAllText(temp,Serializer().Serialize(this),new UTF8Encoding(false));
                if(File.Exists(path)) {
                    try { File.Replace(temp,path,null); }
                    catch(IOException) { MoveOver(temp,path); }
                    catch(PlatformNotSupportedException) { MoveOver(temp,path); }
                } else File.Move(temp,path);
            } finally {
                if(File.Exists(temp)) File.Delete(temp);
            }
        }
        static void MoveOver(string temp,string destination) {
            const int replaceExisting=1,writeThrough=8;
            if(!MoveFileEx(temp,Path.GetFullPath(destination),replaceExisting|writeThrough))
                throw new IOException("Не удалось заменить файл. "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
    }
    static class Seed {
        public static readonly string[] Subjects={"math","physics","russian","other"};
        public static string Subject(string key) {
            switch(key) { case "math":return "Математика"; case "physics":return "Физика"; case "russian":return "Русский язык"; default:return "Свободная доска"; }
        }
        public static Notebook Create(bool sample) {
            var doc=new Notebook { Version=1, Boards=Subjects.Select(s=>Board.Make(Subject(s),s)).ToList() };
            if(!sample) return doc;
            var math=doc.Boards[0].Marks;
            math.Add(Mark.Make("heading",80,109,610,84,"Тригонометрия","Урок 01  /  Функции и единичная окружность"));
            math.Add(Mark.Make("graph",80,227,493,310,"Синусоида","sin(x)","#6548D8"));
            math.Add(Mark.Make("note",600,227,222,192,"Запомнить","sin² x + cos² x = 1\n\nУ синуса и косинуса\nпериод равен 2π.","#FFF1C3"));
            math.Add(Mark.Make("link",600,443,222,94,"Тригонометрия","https://ru.wikipedia.org/wiki/Тригонометрия"));
            math.Add(Mark.Make("text",80,573,493,105,"Свойства синуса","Область определения — все действительные числа.\nМножество значений — от −1 до 1."));
            doc.Boards[1].Marks.AddRange(new []{
                Mark.Make("heading",80,109,600,84,"Механика","Урок 01  /  Силы и движение"),
                Mark.Make("text",80,230,440,150,"Второй закон Ньютона","Равнодействующая сил равна произведению массы на ускорение.\n\nF = ma"),
                Mark.Make("note",547,230,228,190,"У поверхности Земли","g ≈ 9,81 м/с²\n\nСила тяжести — mg.","#DDF4EB")
            });
            doc.Boards[2].Marks.AddRange(new []{
                Mark.Make("heading",80,109,600,84,"Орфография","Урок 01  /  Правописание приставок"),
                Mark.Make("text",80,230,440,177,"Приставки на З и С","Перед звонким согласным в конце приставки пишем з, перед глухим — с.\n\nбезвредный → бесшумный\nразбить → расписать"),
                Mark.Make("note",547,230,228,178,"Приставка С-","Приставка с- пишется одинаково перед звонкими и глухими.\n\nсдать, спеть","#EEE7FF")
            });
            return doc;
        }
    }

    // Objects that span too many tiles are indexed separately.
    sealed class SpatialIndex {
        const int Tile=384;
        readonly Dictionary<long,HashSet<Mark>> tiles=new Dictionary<long,HashSet<Mark>>();
        readonly HashSet<Mark> large=new HashSet<Mark>();
        readonly Dictionary<Mark,long> order=new Dictionary<Mark,long>();
        long sequence;
        static int Cell(float coordinate) { return (int)Math.Floor(coordinate/Tile); }
        static long Key(int x,int y) { return ((long)x<<32)|(uint)y; }
        static IEnumerable<long> Cells(RectangleF rect) {
            for(int x=Cell(rect.Left);x<=Cell(rect.Right);x++)
                for(int y=Cell(rect.Top);y<=Cell(rect.Bottom);y++) yield return Key(x,y);
        }
        static bool IsLarge(RectangleF rect) { return (long)(Cell(rect.Right)-Cell(rect.Left)+1)*(Cell(rect.Bottom)-Cell(rect.Top)+1)>256; }
        internal static RectangleF Area(Mark mark) {
            var area=mark.Bounds;
            float margin=mark.Kind=="arrow"?9+5*mark.Size:9+mark.Size;
            area.Inflate(margin,margin);return area;
        }
        public void Add(Mark mark) { Add(mark,++sequence); }
        void Add(Mark mark,long depth) {
            order.Add(mark,depth);
            var area=Area(mark);
            if(IsLarge(area)) { large.Add(mark); return; }
            foreach(long key in Cells(area)) {
                HashSet<Mark> tile;
                if(!tiles.TryGetValue(key,out tile)) tiles[key]=tile=new HashSet<Mark>();
                tile.Add(mark);
            }
        }
        public void Remove(Mark mark) {
            if(!order.Remove(mark)||large.Remove(mark)) return;
            foreach(long key in Cells(Area(mark))) {
                var tile=tiles[key]; tile.Remove(mark);
                if(tile.Count==0) tiles.Remove(key);
            }
        }
        public void Replace(Mark old,Mark updated) { var depth=order[old]; Remove(old); Add(updated,depth); }
        public IEnumerable<Mark> Query(RectangleF rect) {
            if(IsLarge(rect)) return order.Keys.Where(m=>Area(m).IntersectsWith(rect)).OrderBy(m=>order[m]);
            var hits=new HashSet<Mark>(large);
            foreach(long key in Cells(rect)) {
                HashSet<Mark> tile;
                if(tiles.TryGetValue(key,out tile)) hits.UnionWith(tile);
            }
            return hits.Where(m=>Area(m).IntersectsWith(rect)).OrderBy(m=>order[m]);
        }
    }

    sealed class Formula {
        readonly string input;
        int pos;
        int nesting;
        readonly bool degrees;
        readonly double ans;
        readonly double? parameter;
        Formula(string expr,bool deg,double previous,double? parameter) {
            if(String.IsNullOrWhiteSpace(expr)||expr.Length>512) throw new FormatException("Введите выражение до 512 символов.");
            input=expr.ToLowerInvariant().Replace("−","-").Replace("×","*").Replace("÷","/").Replace("·","*").Replace("π"," pi ").Replace("²","^2").Replace("³","^3").Replace(',','.');
            degrees=deg; ans=previous;this.parameter=parameter;
        }
        public static Func<double,double> Compile(string text,bool degrees=false,double ans=0,double? parameter=null) {
            var p=new Formula(text,degrees,ans,parameter);
            var f=p.Sum();
            if(p.Peek()!='\0') throw new FormatException("Лишний символ: "+p.Peek());
            return f;
        }
        char Peek() { while(pos<input.Length&&Char.IsWhiteSpace(input[pos])) pos++; return pos<input.Length?input[pos]:'\0'; }
        bool Take(char c) { if(Peek()!=c) return false; pos++; return true; }
        Func<double,double> Sum() {
            var value=Product();
            while(true) {
                bool plus=Take('+'); if(!plus&&!Take('-')) return value;
                var left=value; var right=Product(); value=x=>plus?left(x)+right(x):left(x)-right(x);
            }
        }
        Func<double,double> Product() {
            var value=Unary();
            while(true) {
                bool divide=Take('/');
                if(!divide&&!Take('*')) {
                    char c=Peek();
                    if(c!='('&&!Char.IsLetter(c)) return value;
                }
                var left=value; var right=Unary(); value=x=>divide?left(x)/right(x):left(x)*right(x);
            }
        }
        Func<double,double> Unary() {
            if(++nesting>80) throw new FormatException("Слишком много вложенных операций.");
            try {
                if(Take('+')) return Unary();
                if(Take('-')) { var negative=Unary(); return x=>-negative(x); }
                var value=Atom();
                while(Take('!')) { var arg=value; value=x=>Factorial(arg(x)); }
                if(Take('^')) { var exponent=Unary(); return x=>Math.Pow(value(x),exponent(x)); }
                return value;
            } finally { nesting--; }
        }
        Func<double,double> Atom() {
            if(Take('(')) return Parenthesized();
            var c=Peek();
            int start=pos;
            if(Char.IsDigit(c)||c=='.') {
                while(pos<input.Length&&(Char.IsDigit(input[pos])||input[pos]=='.')) pos++;
                if(pos+1<input.Length&&input[pos]=='e') {
                    int end=pos+1; if(input[end]=='+'||input[end]=='-') end++;
                    if(end<input.Length&&Char.IsDigit(input[end])) {
                        while(end<input.Length&&Char.IsDigit(input[end])) end++;
                        pos=end;
                    }
                }
                double number;
                if(!Double.TryParse(input.Substring(start,pos-start),NumberStyles.Float,CultureInfo.InvariantCulture,out number)) throw new FormatException("Некорректное число.");
                return x=>number;
            }
            if(Take('x')) return x=>x;
            while(pos<input.Length&&Char.IsLetter(input[pos])) pos++;
            if(pos==start) throw new FormatException("Ожидалось число, функция или скобка.");
            string name=input.Substring(start,pos-start);
            switch(name) {
                case "pi":return x=>Math.PI;
                case "e":return x=>Math.E;
                case "ans":return x=>ans;
                case "a":if(parameter.HasValue)return x=>parameter.Value;
                    throw new FormatException("Параметр a доступен в редакторе графика.");
            }
            if(!Take('(')) throw new FormatException("Для функции нужны скобки: "+name+"(…).");
            var arg=Parenthesized();
            double factor=degrees?Math.PI/180:1;
            switch(name) {
                case "sin":return x=>Math.Sin(arg(x)*factor);
                case "cos":return x=>Math.Cos(arg(x)*factor);
                case "tan":return x=>Math.Tan(arg(x)*factor);
                case "asin":return x=>Math.Asin(arg(x))/factor;
                case "acos":return x=>Math.Acos(arg(x))/factor;
                case "atan":return x=>Math.Atan(arg(x))/factor;
                case "sqrt":return x=>Math.Sqrt(arg(x));
                case "abs":return x=>Math.Abs(arg(x));
                case "ln":return x=>Math.Log(arg(x));
                case "log":return x=>Math.Log10(arg(x));
                case "exp":return x=>Math.Exp(arg(x));
                case "floor":return x=>Math.Floor(arg(x));
                case "ceil":return x=>Math.Ceiling(arg(x));
                default:throw new FormatException("Неизвестная функция: "+name);
            }
        }
        Func<double,double> Parenthesized() {
            var f=Sum();
            if(!Take(')')) throw new FormatException("Закройте скобку.");
            return f;
        }
        static double Factorial(double n) {
            if(n<0||n>170||Math.Floor(n)!=n) return Double.NaN;
            double result=1; for(int i=2;i<=n;i++) result*=i;
            return result;
        }
    }

    static class Ink {
        public static Mark Finish(List<PointF> points,string color,float size,bool tidy) {
            float minX=points.Min(p=>p.X),maxX=points.Max(p=>p.X),minY=points.Min(p=>p.Y),maxY=points.Max(p=>p.Y);
            float w=Math.Max(1,maxX-minX),h=Math.Max(1,maxY-minY);
            if(tidy&&points.Count>=8&&w>=14&&h>=14) {
                var center=new PointF(minX+w/2,minY+h/2);
                var radii=points.Select(p=>Math.Sqrt(Math.Pow((p.X-center.X)/(w/2),2)+Math.Pow((p.Y-center.Y)/(h/2),2))).ToArray();
                double circumference=0, sweep=0;
                for(int i=1;i<points.Count;i++) {
                    var a=points[i-1]; var b=points[i];
                    circumference+=Distance(a,b);
                    double angle=Math.Atan2(b.Y-center.Y,b.X-center.X)-Math.Atan2(a.Y-center.Y,a.X-center.X);
                    sweep+=Math.Atan2(Math.Sin(angle),Math.Cos(angle));
                }
                if(Distance(points[0],points[points.Count-1])<.31*Math.Min(w,h)&&Math.Abs(sweep)>5.15&&Math.Abs(sweep)<7.1&&
                    circumference<4.8*Math.Max(w,h)&&radii.Average(r=>Math.Abs(r-1))<.17) {
                    if(Math.Min(w,h)/Math.Max(w,h)>.78f) { w=h=(w+h)/2; minX=center.X-w/2; minY=center.Y-h/2; }
                    var ellipse=Mark.Make("ellipse",minX,minY,w,h,color:color); ellipse.Size=size; return ellipse;
                }
            }
            if(tidy&&points.Count>1) {
                var first=points[0]; var last=points[points.Count-1]; var length=Distance(first,last);
                if(length>=22&&points.All(p=>Segment(p,first,last)<Math.Max(2.5,length*.025)))
                    return Line("line",first,last,color,size);
            }
            var mark=Mark.Make("ink",minX,minY,w,h,color:color); mark.Size=size;
            mark.Points=points.SelectMany(p=>new[]{p.X-minX,p.Y-minY}).ToArray();
            return mark;
        }
        public static Mark Line(string kind,PointF a,PointF b,string color,float size) {
            var m=Mark.Make(kind,Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Abs(a.X-b.X),Math.Abs(a.Y-b.Y),color:color);
            m.Size=size; m.Points=new[]{a.X-m.X,a.Y-m.Y,b.X-m.X,b.Y-m.Y}; return m;
        }
        public static double Distance(PointF a,PointF b) { return Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)); }
        public static double Segment(PointF p,PointF a,PointF b) {
            double dx=b.X-a.X,dy=b.Y-a.Y,len=dx*dx+dy*dy;
            if(len==0) return Distance(p,a);
            double t=Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Y-a.Y)*dy)/len));
            return Distance(p,new PointF((float)(a.X+t*dx),(float)(a.Y+t*dy)));
        }
    }
}
