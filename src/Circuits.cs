using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Polya {
    public class CircuitPart {
        public string Kind {get;set;}
        public double Value {get;set;}
        public int Branch {get;set;}
        public bool Open {get;set;}
        public CircuitPart Copy() {return (CircuitPart)MemberwiseClone();}
    }
    static class Circuits {
        static readonly string[] Kinds={"resistor","lamp","switch","capacitor","ammeter"};
        static readonly string[] Names={"Резистор","Лампа","Выключатель","Конденсатор","Амперметр"};
        static readonly Color Wire=Art.C("#263546"),Accent=Art.C("#137E76");
        const int MaxBranch=3, MaxPerBranch=6;
        static CircuitPart Part(string kind,int branch) {
            return new CircuitPart {Kind=kind,Branch=branch,Value=kind=="resistor"?10:kind=="lamp"?20:kind=="capacitor"?100:0};
        }
        public static CircuitPart[] Parts(Mark model) {
            if(model.Circuit!=null)return model.Circuit;
            var initial=Part("resistor",0);
            return model.Text=="parallel"?new[]{initial,Part("resistor",1)}:new[]{initial};
        }
        public static void Validate(Mark mark) {
            var parts=mark.Circuit;
            if(parts!=null&&(parts.Length<1||parts.Length>MaxBranch*MaxPerBranch||
                parts.Any(p=>p==null||!Kinds.Contains(p.Kind)||p.Branch<0||p.Branch>=MaxBranch||
                    Double.IsNaN(p.Value)||p.Value<0||p.Value>1000000||
                    ((p.Kind=="resistor"||p.Kind=="lamp"||p.Kind=="capacitor")&&p.Value<.01))||
                parts.GroupBy(p=>p.Branch).Any(g=>g.Count()>MaxPerBranch)))throw new FormatException("В файле есть некорректная электрическая схема.");
            if(mark.Voltage.HasValue&&(Double.IsNaN(mark.Voltage.Value)||mark.Voltage<0||mark.Voltage>1000))
                throw new FormatException("В файле есть некорректное напряжение источника.");
        }
        static string Number(double n) {return n.ToString("0.###",CultureInfo.CurrentCulture);}
        static string Describe(CircuitPart p) {
            if(p.Kind=="switch") return p.Open?"разомкнут":"замкнут";
            if(p.Kind=="capacitor")return Number(p.Value)+" мкФ";
            if(p.Kind=="resistor"||p.Kind=="lamp")return Number(p.Value)+" Ом";
            return "";
        }
        static string Summary(Mark mark) {
            double conductance=0;
            foreach(var branch in Parts(mark).GroupBy(p=>p.Branch)) {
                if(branch.Any(p=>p.Open&&p.Kind=="switch"||p.Kind=="capacitor"))continue;
                double resistance=branch.Where(p=>p.Kind=="resistor"||p.Kind=="lamp").Sum(p=>p.Value);
                if(resistance<=0)return "Идеальный источник · короткое замыкание";
                conductance+=1/resistance;
            }
            if(conductance==0)return "Постоянный ток после установления: 0 А";
            return "Rэкв = "+Number(1/conductance)+" Ом    ·    I = "+Number((mark.Voltage??9)*conductance)+" А";
        }
        static void WireLine(Graphics g,float x,float y,float endX,float endY) {Art.Stroke(g,Wire,1.6f,x,y,endX,endY);}
        internal sealed class Hit {
            public RectangleF Rect;
            public int Index,Branch,Slot;
            public bool Adding;
        }
        public static void Draw(Graphics g,RectangleF area,Mark mark,int selected) {
            DrawInteractive(g,area,mark,selected,false,null);
        }
        static void DrawInteractive(Graphics g,RectangleF area,Mark mark,int selected,bool adding,List<Hit> hits) {
            var items=Parts(mark);
            float scale=Math.Min(area.Width/252,area.Height/140);
            if(scale<=0)return;
            var state=g.Save();
            float originX=area.X+(area.Width-252*scale)/2,originY=area.Y+(area.Height-140*scale)/2;
            g.TranslateTransform(originX,originY);
            g.ScaleTransform(scale,scale);
            Func<float,float,float,float,RectangleF> hitRect=(x,y,w,h)=>new RectangleF(originX+x*scale,originY+y*scale,w*scale,h*scale);
            var branches=items.Select((p,i)=>new {Part=p,Index=i}).GroupBy(x=>x.Part.Branch).OrderBy(x=>x.Key).ToArray();
            const float left=9,right=243,baseY=112,center=126;
            float start=branches.Length==1?51:26;
            WireLine(g,left,start,left,baseY);WireLine(g,right,start,right,baseY);
            if(hits!=null) {
                if(selected<0&&!adding) Art.Round(g,new RectangleF(center-13,baseY-12,26,24),6,Art.C("#DBF2EB"));
                hits.Add(new Hit{Index=-1,Rect=hitRect(center-14,baseY-14,28,28)});
            }
            WireLine(g,left,baseY,center-4,baseY);WireLine(g,center+4,baseY,right,baseY);
            WireLine(g,center-4,baseY-9,center-4,baseY+9);WireLine(g,center+4,baseY-5,center+4,baseY+5);
            Art.Label(g,"+"+Number(mark.Voltage??9)+" В",70,baseY+5,50,16,10,Wire,true,StringAlignment.Far);
            for(int row=0;row<branches.Length;row++) {
                var branch=branches[row].ToArray();
                float y=start+row*(branches.Length==2?48:30);
                float step=(right-left)/branch.Length;
                float from=left;
                for(int i=0;i<branch.Length;i++) {
                    float mid=left+(i+.5f)*step;
                    var item=branch[i];
                    float half=Math.Min(12,step*.38f);
                    if(item.Index==selected&&!adding)Art.Round(g,new RectangleF(mid-half-3,y-11,2*half+6,22),5,Art.C("#DBF2EB"));
                    WireLine(g,from,y,mid-half,y);
                    if(hits!=null)hits.Add(new Hit{Index=item.Index,Branch=item.Part.Branch,Rect=hitRect(mid-half-3,y-13,2*half+6,26)});
                    Symbol(g,item.Part,mid,y,half);
                    if(mark.Labels??true) {
                        string prefix=item.Part.Kind=="resistor"?"R":item.Part.Kind=="lamp"?"L":item.Part.Kind=="capacitor"?"C":item.Part.Kind=="switch"?"S":"A";
                        string label=prefix+items.Take(item.Index+1).Count(p=>p.Kind==item.Part.Kind);
                        if(branch.Length<5&&(item.Part.Kind=="resistor"||item.Part.Kind=="lamp"||item.Part.Kind=="capacitor"))label+=" · "+Describe(item.Part);
                        Art.Label(g,label,mid-step/2,y-24,step,12,8.8f,Wire,false,StringAlignment.Center);
                    }
                    from=mid+half;
                }
                WireLine(g,from,y,right,y);
                if(branches.Length>1) {Art.Dot(g,Accent,left,y,2.2f);Art.Dot(g,Accent,right,y,2.2f);}
                if(adding&&hits!=null&&branch.Length<MaxPerBranch) {
                    for(int slot=0;slot<=branch.Length;slot++) {
                        float x=slot==0?left+10:slot==branch.Length?right-10:left+step*slot;
                        Art.Dot(g,Art.C("#ECF8F5"),x,y,6.5f);Art.Circle(g,Accent,x,y,6.1f,1.05f);
                        Art.Stroke(g,Accent,1.05f,x-3,y,x+3,y);Art.Stroke(g,Accent,1.05f,x,y-3,x,y+3);
                        hits.Insert(0,new Hit{Adding=true,Branch=branches[row].Key,Slot=slot,Rect=hitRect(x-8,y-9,16,18)});
                    }
                }
            }
            if(mark.Measurements)Art.Label(g,Summary(mark),6,127,240,13,8.7f,Accent,true,StringAlignment.Center);
            g.Restore(state);
        }
        static void Symbol(Graphics g,CircuitPart p,float x,float y,float half) {
            float l=x-half,r=x+half;
            switch(p.Kind) {
                case "resistor":
                    Art.Round(g,new RectangleF(l,y-4,r-l,8),1,Color.Transparent,Wire);break;
                case "lamp":case "ammeter":
                    float radius=Math.Min(7,half);
                    WireLine(g,l,y,x-radius,y);WireLine(g,x+radius,y,r,y);
                    Art.Circle(g,Wire,x,y,radius,1.5f);
                    if(p.Kind=="lamp") {
                        WireLine(g,x-4,y-4,x+4,y+4);WireLine(g,x+4,y-4,x-4,y+4);
                    } else Art.Label(g,"A",x-7,y-8,14,16,9,Wire,true,StringAlignment.Center);
                    break;
                case "switch":
                    Art.Dot(g,Wire,l,y,1.8f);Art.Dot(g,Wire,r,y,1.8f);
                    WireLine(g,l,y,p.Open?r-3:r,p.Open?y-9:y);break;
                case "capacitor":
                    WireLine(g,l,y,x-3,y);WireLine(g,x+3,y,r,y);
                    WireLine(g,x-3,y-6,x-3,y+6);WireLine(g,x+3,y-6,x+3,y+6);
                    break;
            }
        }
        public static void Edit(Workspace owner,Mark mark,Action<Mark> done) {
            var sheet=new CircuitEditor(mark);
            sheet.OnBoard(owner,result=>done(result==DialogResult.OK?sheet.Result:null));
        }
        sealed class CircuitEditor : Sheet {
            readonly List<CircuitPart> items;
            readonly List<Hit> hits=new List<Hit>();
            readonly Button[] tools=new Button[Kinds.Length];
            readonly Button[] branches=new Button[MaxBranch];
            readonly DiagramPane preview;
            readonly Label selectedTitle,selectedDetail,fieldLabel,branchLabel,summary,hint;
            readonly TextBox value;
            readonly Button sourceButton,earlier,later,remove,switchButton,cancelInsert;
            int selected=-1,adding=-1;
            bool syncing;
            public Mark Result {get;private set;}
            public CircuitEditor(Mark mark):base("Электрическая цепь","Последовательные соединения и до трёх параллельных ветвей",592,740) {
                Result=mark.Copy();Result.Measurements=true;
                items=Parts(mark).Select(p=>p.Copy()).ToList();Result.Circuit=items.ToArray();
                int x=22;
                int[] widths={95,80,120,120,104};
                for(int i=0;i<Kinds.Length;i++) {
                    int tool=i;
                    tools[i]=Action("+ "+Names[i],x,97,widths[i],()=>ChooseTool(tool));
                    x+=widths[i]+5;
                }
                Action("Новая параллельная ветвь",22,134,268,AddBranch);
                preview=Put(new DiagramPane{BackColor=Art.C("#F5F8FA"),AccessibleName="Схема цепи"},22,176,548,282);
                preview.Render=(g,r)=>{
                    hits.Clear();
                    var drawing=Result.Copy();drawing.Measurements=false;
                    DrawInteractive(g,new RectangleF(6,8,r.Width-12,r.Height-16),drawing,selected,adding>=0,hits);
                };
                preview.MouseMove+=(s,e)=>preview.Cursor=hits.Any(h=>h.Rect.Contains(e.Location))?Cursors.Hand:Cursors.Default;
                preview.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)Pick(e.Location);};
                selectedTitle=Caption("",22,474,269);
                selectedTitle.Font=new Font("Segoe UI",13,FontStyle.Bold);selectedTitle.ForeColor=Art.Ink;
                selectedDetail=Caption("",22,501,278);
                fieldLabel=Caption("",22,534,246);
                value=Field("",22,559,246);
                value.TextChanged+=(s,e)=>ApplyValue();
                branchLabel=Caption("Перенести в ветвь",290,474,268);
                sourceButton=Action("Источник питания",290,594,268,()=>SelectPart(-1));
                for(int i=0;i<MaxBranch;i++) {
                    int branch=i;
                    branches[i]=Action("Ветвь "+(i+1),290+i*90,501,87,()=>MoveBranch(branch));
                }
                switchButton=Action("",22,559,246,()=>{items[selected].Open=!items[selected].Open;RefreshEditor(true);});
                cancelInsert=Action("Отменить добавление",290,134,268,()=>{adding=-1;RefreshEditor(true);});
                earlier=Action("←",290,550,43,()=>MovePart(-1));earlier.AccessibleName="Переместить влево";
                later=Action("→",339,550,43,()=>MovePart(1));later.AccessibleName="Переместить вправо";
                remove=Action("Удалить",391,550,167,Remove);
                summary=Caption("",22,625,548);summary.ForeColor=Accent;
                hint=Caption("",22,648,548);hint.ForeColor=Art.Muted;
                Footer("Применить",()=>ValidateInput(true));
                RefreshEditor(true);
            }
            protected override bool ProcessCmdKey(ref Message message,Keys keyData) {
                if(adding>=0&&keyData==Keys.Escape) {
                    adding=-1;RefreshEditor(true);return true;
                }
                return base.ProcessCmdKey(ref message,keyData);
            }
            void ChooseTool(int tool) {
                if(!ValidateInput(false))return;
                adding=adding==tool?-1:tool;
                RefreshEditor(true);
            }
            void SelectPart(int index) {
                if(!ValidateInput(false))return;
                selected=index;adding=-1;RefreshEditor(true);
            }
            void Pick(PointF point) {
                var hit=hits.FirstOrDefault(h=>h.Rect.Contains(point));
                if(hit==null)return;
                if(hit.Adding&&adding>=0) {Insert(hit.Branch,hit.Slot,adding);return;}
                SelectPart(hit.Index);
            }
            void AddBranch() {
                if(!ValidateInput(false))return;
                int branch=Enumerable.Range(0,MaxBranch).FirstOrDefault(i=>items.All(p=>p.Branch!=i));
                if(items.Any(p=>p.Branch==branch)) {hint.Text="В схеме уже три ветви.";return;}
                Insert(branch,0,adding<0?0:adding);
            }
            void Insert(int branch,int slot,int kind) {
                var positions=items.Select((p,i)=>new{p,i}).Where(x=>x.p.Branch==branch).Select(x=>x.i).ToArray();
                if(positions.Length>=MaxPerBranch){hint.Text="В ветви уже шесть компонентов.";return;}
                int at=slot<positions.Length?positions[slot]:positions.Length==0?items.Count:positions.Last()+1;
                items.Insert(at,Part(Kinds[kind],branch));
                selected=at;adding=-1;RefreshEditor(true);
            }
            void MoveBranch(int branch) {
                if(selected<0||!ValidateInput(false)||items[selected].Branch==branch)return;
                if(items.Count(p=>p.Branch==branch)>=MaxPerBranch){hint.Text="В ветви уже шесть компонентов.";return;}
                items[selected].Branch=branch;RefreshEditor(true);
            }
            int Neighbor(int step) {
                if(selected<0)return -1;
                for(int i=selected+step;i>=0&&i<items.Count;i+=step)
                    if(items[i].Branch==items[selected].Branch)return i;
                return -1;
            }
            void MovePart(int step) {
                int next=Neighbor(step);
                if(next<0||!ValidateInput(false))return;
                var part=items[selected];items[selected]=items[next];items[next]=part;selected=next;
                RefreshEditor(true);
            }
            void Remove() {
                if(selected<0)return;
                if(items.Count==1){hint.Text="В цепи должен остаться хотя бы один компонент.";return;}
                items.RemoveAt(selected);selected=Math.Min(selected,items.Count-1);RefreshEditor(true);
            }
            bool HasValue {
                get {return selected<0||items[selected].Kind=="resistor"||items[selected].Kind=="lamp"||items[selected].Kind=="capacitor";}
            }
            bool ReadValue(out double number) {
                return DiagramEdit.Parse(value.Text,out number)&&number>=(selected<0?0:.01)&&number<=(selected<0?1000:1000000);
            }
            bool ValidateInput(bool dialog) {
                if(!HasValue)return true;
                double number;
                if(ReadValue(out number))return true;
                string message=selected<0?"Введите напряжение от 0 до 1000 В.":"Введите номинал от 0,01 до 1 000 000.";
                if(dialog)return Error(message);
                hint.Text=message;return false;
            }
            void ApplyValue() {
                double number;
                if(syncing||!HasValue||!ReadValue(out number))return;
                if(selected<0)Result.Voltage=number;else items[selected].Value=number;
                RefreshEditor(false);
            }
            void RefreshEditor(bool updateField) {
                Result.Circuit=items.ToArray();
                var part=selected<0?null:items[selected];
                selectedTitle.Text=part==null?"Источник питания":Names[Array.IndexOf(Kinds,part.Kind)];
                string detail=part==null?"":Describe(part);
                selectedDetail.Text=part==null?"Источник постоянного напряжения":"Ветвь "+(part.Branch+1)+(detail.Length>0?"  ·  "+detail:"");
                fieldLabel.Text=part==null?"Напряжение, В":part.Kind=="capacitor"?"Ёмкость, мкФ":HasValue?"Сопротивление, Ом":"";
                value.Parent.Visible=HasValue&&adding<0;
                if(updateField) {
                    syncing=true;
                    value.Text=part==null?Number(Result.Voltage??9):Number(part.Value);
                    syncing=false;
                }
                fieldLabel.Visible=adding<0;
                sourceButton.Visible=part!=null&&adding<0;
                branchLabel.Visible=part!=null&&adding<0;
                cancelInsert.Visible=adding>=0;
                for(int i=0;i<branches.Length;i++) {
                    branches[i].Visible=part!=null&&adding<0;
                    branches[i].BackColor=part!=null&&part.Branch==i?Art.Pale:Art.White;
                }
                earlier.Visible=later.Visible=remove.Visible=part!=null&&adding<0;
                earlier.Enabled=Neighbor(-1)>=0;later.Enabled=Neighbor(1)>=0;
                switchButton.Visible=part!=null&&part.Kind=="switch"&&adding<0;
                if(switchButton.Visible)switchButton.Text=part.Open?"Замкнуть выключатель":"Разомкнуть выключатель";
                for(int i=0;i<tools.Length;i++)tools[i].BackColor=adding==i?Art.Pale:Art.White;
                if(adding>=0) {
                    selectedTitle.Text=Names[adding];
                    selectedDetail.Text="Выберите «+» на проводе";
                }
                summary.Text=Summary(Result);
                hint.Text=adding>=0?"Нажмите «+» на проводе или начните параллельную ветвь.":"Щёлкните по детали или источнику, чтобы изменить его.";
                preview.Invalidate();
            }
        }
    }
}
