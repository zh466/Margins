using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Polya {
    static class Catalog {
        internal sealed class Group {
            public readonly string Id,Icon,Name;
            public Group(string id,string icon,string name) {Id=id;Icon=icon;Name=name;}
        }
        internal sealed class Item {
            public readonly string Id,Name;
            public Item(string id,string name) {Id=id;Name=name;}
        }
        sealed class Category {
            public readonly string Name; public readonly Item[] Items;
            public Category(string name,params string[] rows) {
                Name=name;Items=rows.Select(row=>{var parts=row.Split('|');return new Item(parts[0],parts[1]);}).ToArray();
            }
        }
        static readonly Group algebra=new Group("algebra","graph","Алгебра"),
            geometry=new Group("geometry","geometry","Геометрия"), calculator=new Group("calc","calc","Калькулятор"),
            reference=new Group("book","book","Справочник"), constants=new Group("book","book","Постоянные СИ"),
            graphs=new Group("graph","graph","График функции"),
            mechanics=new Group("mechanics","physics","Схемы по физике"), syntax=new Group("syntax","scheme","Синтаксические схемы"),
            spelling=new Group("book","book","Шпаргалки");
        public static Group[] Tools(string subject) {
            switch(subject) {
                case "math":return new[]{calculator,algebra,geometry,reference};
                case "physics":return new[]{calculator,mechanics,constants,graphs};
                case "russian":return new[]{syntax,spelling};
                default:return new[]{calculator,algebra,geometry};
            }
        }
        static readonly Dictionary<string,Category[]> Groups=new Dictionary<string,Category[]> {
            {"algebra",new[]{new Category("Алгебра","graph|График функции","trig|Триг. окружность","system|Система уравнений","number|Числовая прямая","plane|Координатная плоскость","interval|Числовой промежуток","quadratic|Квадратное уравнение","inequality|Знаки неравенств")}},
            {"geometry",new[]{
                new Category("Планиметрия","triangle|Треугольник","right-triangle|Прямоугольный треугольник","iso|Равнобедренный треугольник","equilateral|Равносторонний треугольник",
                    "altitude|Высота треугольника","median|Медиана треугольника","bisector|Биссектриса","midline|Средняя линия",
                    "square|Квадрат","rectangle|Прямоугольник","rhombus|Ромб","parallelogram|Параллелограмм","trapezoid|Трапеция",
                    "polygon|Правильный шестиугольник","circle|Окружность","sector|Сектор и хорда","tangent|Касательная","inscribed|Вписанная окружность","circumcircle|Описанная окружность","angles|Параллельные прямые"),
                new Category("Стереометрия","cube|Куб","box|Параллелепипед","prism|Треугольная призма","hex-prism|Шестиугольная призма","pyramid|Четырёхугольная пирамида",
                    "tetrahedron|Тетраэдр","cylinder|Цилиндр","cone|Конус","frustum|Усечённый конус","frustum-pyramid|Усечённая пирамида","sphere|Шар",
                    "section|Сечение куба","net|Развёртка куба","planes|Пересекающиеся плоскости")
            }},
            {"mechanics",new[]{new Category("Механика","forces|Силы на наклонной","lever|Рычаг","projectile|Бросок под углом","spring|Пружина и груз","pendulum|Маятник"),
                new Category("Электричество и оптика","circuit|Простая электрическая цепь","parallel|Параллельное соединение","rays|Отражение луча","lens|Собирающая линза")}},
            {"syntax",new[]{new Category("Орфография и синтаксис","word|Состав слова","simple|Грамматическая основа","compound|Сложносочинённое","subordinate|Сложноподчинённое","speech|Прямая речь","homogeneous|Однородные члены","turn|Обособленный оборот")}}
        };
        static readonly Mark DemoGraph=Mark.Make("graph",0,0,200,100,"","sin(x)");
        public static void Pick(Workspace owner,Group group,Action<Item> done) {
            var sheet=new Sheet(group.Name,"Выберите рисунок для вставки на доску",730,571);
            var categories=Groups[group.Id]; Item selected=null;
            var panel=sheet.Put(new FlowLayoutPanel{AutoScroll=true,WrapContents=true,BackColor=Art.White},19,143,695,411);
            var heading=new List<Button>();
            Action<int> render=null;
            for(int i=0;i<categories.Length;i++) {
                int category=i;
                heading.Add(sheet.Action(categories[i].Name,22+i*195,98,187,()=>render(category)));
            }
            render=index=>{
                panel.SuspendLayout();
                while(panel.Controls.Count>0) panel.Controls[0].Dispose();
                for(int i=0;i<heading.Count;i++) {
                    heading[i].BackColor=i==index?Art.Pale:Art.White;
                    heading[i].ForeColor=i==index?Art.Purple:Art.Ink;
                }
                foreach(var item in categories[index].Items) {
                    var tile=new DiagramPane{Size=new Size(216,119),Margin=new Padding(4,3,5,7),Cursor=Cursors.Hand};
                    bool hovered=false;
                    tile.Render=(g,r)=>{
                        Art.Round(g,new RectangleF(1,1,r.Width-2,r.Height-2),12,hovered?Art.Pale:Art.C("#F8F7FC"),Art.Line);
                        if(item.Id=="graph") GraphArt.Plot(g,new RectangleF(8,0,r.Width-16,90),DemoGraph,Art.Purple);
                        else if(item.Id=="trig") GraphArt.Circle(g,new RectangleF(8,-4,r.Width-16,104),40);
                        else {
                            var sample=Mark.Make("diagram",12,2,r.Width-24,85,"",item.Id);
                            sample.Labels=false;DiagramEdit.Draw(g,sample);
                        }
                        Art.Label(g,item.Name,6,90,r.Width-12,23,12,Art.Ink,true,StringAlignment.Center);
                    };
                    tile.MouseEnter+=(s,e)=>{hovered=true;tile.Invalidate();};
                    tile.MouseLeave+=(s,e)=>{hovered=false;tile.Invalidate();};
                    tile.Click+=(s,e)=>{selected=item;sheet.DialogResult=DialogResult.OK;};
                    panel.Controls.Add(tile);
                }
                panel.AutoScrollPosition=Point.Empty;panel.ResumeLayout();
            };
            render(0);
            sheet.OnBoard(owner,result=>done(result==DialogResult.OK?selected:null));
        }
        public static void Draw(Graphics g,string id,RectangleF card,string title) {
            DiagramEdit.Draw(g,Mark.Make("diagram",card.X,card.Y,card.Width,card.Height,title,id));
        }
        internal static void Sketch(Graphics g,string id,RectangleF pad,bool labels,Color? outline=null) {
            if(String.IsNullOrEmpty(id)) return;
            var state=g.Save();
            float size=Math.Min(pad.Width/260,pad.Height/150);
            if(size<=0) {g.Restore(state);return;}
            g.TranslateTransform(pad.X+(pad.Width-size*260)/2,pad.Y+(pad.Height-size*150)/2);
            g.ScaleTransform(size,size);
            Color c=outline??Art.Purple,light=Art.C("#8995A1"),guide=Art.C("#167F76");
            Action<float[]> p=v=>Art.Stroke(g,c,2.5f,v);
            Action<float[]> a=v=>Art.Stroke(g,guide,2,v);
            Action<float[]> d=v=>{
                using(var pen=Art.Pen(light,1.6f)) {
                    pen.DashStyle=DashStyle.Dash;
                    g.DrawLines(pen,Enumerable.Range(0,v.Length/2).Select(i=>new PointF(v[2*i],v[2*i+1])).ToArray());
                }
            };
            Action<string,float,float> label=(t,x,y)=>Art.Label(g,t,x,y,200,22,16,Art.Ink,true);
            switch(id) {
                case "triangle":case "iso":case "equilateral":case "median":case "altitude":case "midline":case "bisector":case "inscribed":case "circumcircle":
                    float tx=id=="triangle"||id=="altitude"||id=="median"?96:130;
                    if(id=="circumcircle") Art.Circle(g,light,130,83,65,1.6f);
                    float top=id=="circumcircle"?18:24,edge=id=="equilateral"?191.2f:207,baseY=id=="circumcircle"?116:130;
                    if(id=="circumcircle") p(new float[]{130,top,74,baseY,186,baseY,130,top});
                    else p(new float[]{tx,top,260-edge,baseY,edge,baseY,tx,top});
                    if(id=="altitude") {a(new float[]{tx,top,tx,baseY});a(new float[]{tx,baseY-10,tx+10,baseY-10,tx+10,baseY});}
                    if(id=="median") a(new float[]{tx,top,130,baseY});
                    if(id=="midline") a(new float[]{91.5f,77,168.5f,77});
                    if(id=="bisector") {a(new float[]{130,top,130,baseY});using(var pen=Art.Pen(guide,1.4f))g.DrawArc(pen,109,16,42,42,55,70);}
                    if(id=="inscribed") Art.Circle(g,guide,130,91,39,1.8f);
                    if(labels) {label("A",tx-6,0);label("B",id=="circumcircle"?61:249-edge,baseY);label("C",id=="circumcircle"?191:edge+4,baseY);}
                    break;
                case "right-triangle":p(new float[]{63,22,63,125,202,125,63,22});a(new float[]{63,112,77,112,77,125});break;
                case "square":p(new float[]{76,21,184,21,184,129,76,129,76,21});break;
                case "rectangle":p(new float[]{45,28,215,28,215,122,45,122,45,28});break;
                case "rhombus":p(new float[]{130,18,218,76,130,134,42,76,130,18});d(new float[]{42,76,218,76});d(new float[]{130,18,130,134});break;
                case "parallelogram":p(new float[]{82,28,217,28,178,123,43,123,82,28});d(new float[]{82,28,82,123});break;
                case "trapezoid":p(new float[]{85,25,175,25,219,128,41,128,85,25});break;
                case "polygon":p(new float[]{92,19,168,19,206,75,168,131,92,131,54,75,92,19});break;
                case "circle":case "sector":case "tangent":
                    Art.Circle(g,c,129,75,53,2.5f);Art.Dot(g,c,129,75,3);
                    a(new float[]{129,75,172,106});
                    if(id=="sector") {a(new float[]{129,75,172,44,172,106});}
                    if(id=="tangent") {a(new float[]{182,12,182,138});d(new float[]{129,75,182,75});}
                    if(labels)label("O",112,75);
                    break;
                case "angles":p(new float[]{35,48,220,48});p(new float[]{35,104,220,104});a(new float[]{75,137,177,15});break;
                case "cube":case "box":case "section":
                    float l=id=="box"?37:61,r=id=="box"?183:159;
                    p(new float[]{l,56,r,56,r,134,l,134,l,56,l+42,19,r+42,19,r,56});
                    p(new float[]{r+42,19,r+42,97,r,134});
                    d(new float[]{l+42,19,l+42,97,r+42,97});d(new float[]{l,134,l+42,97});
                    if(id=="section")a(new float[]{l,92,r,92,r+42,55,l+42,55,l,92});
                    break;
                case "prism":p(new float[]{71,48,166,48,201,20,71,48,71,133,166,133,201,105,201,20});p(new float[]{166,48,166,133});d(new float[]{71,133,201,105});break;
                case "hex-prism":
                    p(new float[]{70,35,106,18,158,18,193,35,158,53,106,53,70,35,70,112,106,130,158,130,193,112,193,35});
                    p(new float[]{106,53,106,130});p(new float[]{158,53,158,130});d(new float[]{70,112,106,95,158,95,193,112});break;
                case "pyramid":case "tetrahedron":case "frustum-pyramid":
                    p(new float[]{42,124,146,138,213,102});
                    if(id=="tetrahedron")d(new float[]{42,124,213,102});else d(new float[]{42,124,108,94,213,102});
                    if(id=="frustum-pyramid") p(new float[]{42,124,85,51,152,58,190,37,124,30,85,51,152,58,146,138,213,102,190,37});
                    else {p(new float[]{42,124,129,13,146,138});p(new float[]{129,13,213,102});if(id=="pyramid")d(new float[]{108,94,129,13});}
                    break;
                case "cylinder":case "cone":case "frustum":case "sphere":
                    using(var pen=Art.Pen(c,2.5f)) {
                        if(id=="sphere") {g.DrawEllipse(pen,72,17,116,116);g.DrawArc(pen,72,57,116,36,0,180);}
                        else {
                            g.DrawArc(pen,71,112,118,24,0,180);
                            if(id=="cylinder") {g.DrawEllipse(pen,71,19,118,24);p(new float[]{71,31,71,124});p(new float[]{189,31,189,124});}
                            if(id=="cone")p(new float[]{71,124,130,17,189,124});
                            if(id=="frustum") {g.DrawEllipse(pen,94,25,72,20);p(new float[]{71,124,94,35});p(new float[]{189,124,166,35});}
                        }
                    }
                    using(var pen=Art.Pen(light,1.6f)) {
                        pen.DashStyle=DashStyle.Dash;
                        if(id=="sphere")g.DrawArc(pen,72,57,116,36,180,180);else g.DrawArc(pen,71,112,118,24,180,180);
                    }
                    break;
                case "net":
                    using(var pen=Art.Pen(c,2)) foreach(var cell in new[]{new Point(0,1),new Point(1,1),new Point(2,1),new Point(3,1),new Point(1,0),new Point(1,2)})
                        g.DrawRectangle(pen,55+cell.X*36,20+cell.Y*36,36,36);
                    break;
                case "planes":p(new float[]{26,102,188,102,230,64,67,64,26,102});a(new float[]{79,129,139,15,188,15,128,129,79,129});break;
                case "plane":case "number":case "interval":
                    Arrow(g,c,27,83,232,83);
                    for(int i=0;i<9;i++) p(new float[]{45+20*i,78,45+20*i,88});
                    if(id=="plane"){Arrow(g,c,125,133,125,14);if(labels)label("y",135,12);}
                    if(id=="interval"){a(new float[]{65,83,185,83});Art.Circle(g,guide,65,83,5,2);Art.Dot(g,guide,185,83,5);}
                    if(labels)label("x",227,86);
                    break;
                case "forces":
                    p(new float[]{38,125,215,125,215,48,38,125});p(new float[]{112,94,133,85,124,63,103,72,112,94});
                    Arrow(g,guide,119,79,119,121);Arrow(g,guide,119,79,100,40);break;
                case "lever":p(new float[]{39,66,222,66});p(new float[]{112,105,130,68,149,105,112,105});Arrow(g,guide,54,25,54,64);Arrow(g,guide,206,25,206,64);break;
                case "projectile":p(new float[]{24,124,235,124});using(var pen=Art.Pen(c,2.5f))g.DrawBezier(pen,40,124,93,-10,158,-10,215,124);Arrow(g,guide,39,124,72,71);break;
                case "spring":p(new float[]{80,20,180,20});p(new float[]{130,20,130,30,115,38,145,46,115,54,145,62,115,70,145,78,130,87,130,94});p(new float[]{113,94,147,94,147,125,113,125,113,94});break;
                case "pendulum":p(new float[]{85,22,174,22});p(new float[]{129,22,171,109});d(new float[]{129,22,129,129});Art.Dot(g,c,171,109,10);break;
                case "circuit":case "parallel":
                    p(new float[]{114,120,48,120,48,39,92,39});p(new float[]{167,39,211,39,211,120,145,120});
                    p(new float[]{114,105,114,135});p(new float[]{145,111,145,129});
                    p(new float[]{92,39,92,29,167,29,167,49,92,49,92,39});
                    if(id=="parallel"){a(new float[]{48,70,92,70,92,60,167,60,167,80,92,80,92,70});a(new float[]{167,70,211,70});}
                    break;
                case "rays":p(new float[]{35,105,224,105});d(new float[]{130,104,130,22});Arrow(g,guide,56,36,130,105);Arrow(g,guide,130,105,204,36);break;
                case "lens":p(new float[]{25,74,235,74});Arrow(g,c,130,74,130,17);Arrow(g,c,130,74,130,133);a(new float[]{38,46,130,46,222,99});a(new float[]{38,46,130,74,222,102});break;
                default:Notation(g,id);break;
            }
            g.Restore(state);
        }
        static void Arrow(Graphics g,Color c,float x,float y,float ex,float ey) {
            double angle=Math.Atan2(ey-y,ex-x);float s=9;
            Art.Stroke(g,c,2,x,y,ex,ey);
            Art.Stroke(g,c,2,ex-s*(float)Math.Cos(angle-.45),ey-s*(float)Math.Sin(angle-.45),ex,ey,
                ex-s*(float)Math.Cos(angle+.45),ey-s*(float)Math.Sin(angle+.45));
        }
        static void Notation(Graphics g,string id) {
            string top="",bottom="";
            switch(id) {
                case "system":
                    Art.Label(g,"{",33,14,66,116,94,Art.Purple);
                    Art.Label(g,"a₁x + b₁y = c₁",83,31,160,40,23,Art.Ink);
                    Art.Label(g,"a₂x + b₂y = c₂",83,76,160,40,23,Art.Ink);return;
                case "quadratic":top="ax² + bx + c = 0";bottom="D = b² − 4ac";break;
                case "inequality":top="a < b    a ≤ b";bottom="a > b    a ≥ b";break;
                case "word":top="приставка  корень  суффикс";bottom="основа + окончание";break;
                case "simple":top="[  подлежащее  —  сказуемое  ]";bottom="Грамматическая основа";break;
                case "compound":top="[  …  ],  и  [  …  ].";bottom="Две равноправные части";break;
                case "subordinate":top="[  …  ],  (  потому что  …  ).";bottom="Главная и придаточная части";break;
                case "speech":top="А: «П».";bottom="«П», — а.";break;
                case "homogeneous":top="○,  ○  и  ○";bottom="Однородные члены";break;
                case "turn":top="[ …,  |оборот|,  … ].";bottom="Обособление запятыми";break;
            }
            Art.Label(g,top,0,24,260,49,id=="word"||id=="simple"?17:23,Art.Purple,true,StringAlignment.Center);
            Art.Label(g,bottom,0,79,260,35,16,Art.Muted,false,StringAlignment.Center);
        }
    }
}
