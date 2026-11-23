using System;
using System.Drawing;
using System.Windows.Forms;

namespace Polya {
    sealed partial class Workspace {
        static readonly string[] ToolIds={"hand","select","rect","diamond","ellipse","arrow","line","pen","text","note","eraser"};
        static readonly string[] ToolNames={"Рука · H","Выделение · V / 1","Прямоугольник · R / 2","Ромб · D / 3",
            "Эллипс · O / 4","Стрелка · A / 5","Линия · L / 6","Перо · P / 7","Текст · T / 8","Заметка · N","Ластик · E"};
        static readonly string[] ToolKeys={"H","V","R","D","O","A","L","P","T","N","E"};
        static readonly string[] Fills={"","#EEE7FF","#F9DFE9","#DDF4EB","#E1EEFE","#FFF1C3"};
        static readonly string[] NoteFills={"#FFFFFF","#EEE7FF","#F9DFE9","#DDF4EB","#E1EEFE","#FFF1C3"};
        RectangleF strip;
        void DrawToolStrip(Graphics g) {
            float step=Math.Max(25,Math.Min(37,(screenW-140)/13f));
            float width=step*13+25;
            strip=R((screenW-width)/2,15,width,45);
            Art.Round(g,R(strip.X,strip.Y+2,strip.Width,strip.Height),11,Color.FromArgb(14,32,32,54));
            Art.Round(g,strip,11,Art.White,Art.Line);
            Target(strip,()=>{});
            Func<int,RectangleF> slot=index=>R(strip.X+6+index*step+(index>0?6:0),strip.Y+5,step-2,35);
            // The separator keeps the tool lock distinct from drawing modes.
            ToolGlyph(g,slot(0),surface.KeepTool?"lock":"unlock","Сохранять инструмент активным · Q",null,
                ()=>surface.KeepTool=!surface.KeepTool,surface.KeepTool);
            Art.Stroke(g,Art.Line,1,strip.X+step+8,strip.Y+11,strip.X+step+8,strip.Bottom-11);
            for(int i=0;i<ToolIds.Length;i++) {
                string id=ToolIds[i];
                ToolGlyph(g,slot(i+1),id,ToolNames[i],step>=32?ToolKeys[i]:null,()=>ChooseTool(id),surface.Tool==id);
            }
            ToolGlyph(g,slot(12),"more","Дополнительные инструменты",null,()=>TogglePopup("more"),popup=="more"||surface.Tool=="link");
            var options=R(18,77,41,40);
            Button(g,options,"background",null,()=>TogglePopup("background"),"Фон доски",active:popup=="background");
        }
        void ToolGlyph(Graphics g,RectangleF r,string icon,string name,string key,Action action,bool selected) {
            if(selected||Hover(r)) Art.Round(g,r,8,selected?Art.Pale:Art.Sidebar);
            Art.Icon(g,icon,r.X+(r.Width-17)/2,r.Y+7,17,selected?Art.C("#454099"):Art.Ink);
            if(key!=null) Art.Label(g,key,r.Right-11,r.Bottom-12,9,10,8,selected?Art.Purple:Art.Fade,false,StringAlignment.Center);
            Target(r,action,name);
        }
        void DrawToolOptions(Graphics g) {
            var current=surface.Tool=="select"?surface.Selected:null;
            string kind=current==null?surface.Tool:current.Kind;
            if(current!=null&&kind=="diagram") {
                if(popup!="background"&&popup!="main"&&popup!="boards")DrawDiagramOptions(g,current);
                return;
            }
            if(current!=null&&kind=="image") {
                if(popup!="background"&&popup!="main"&&popup!="boards") {
                    var boxImage=R(14,73,219,122);
                    PopupBase(g,boxImage,"Изображение");
                    Button(g,R(25,118,196,33),null,current.RemoveWhite?"Вернуть фон":"Убрать белый фон",
                        ()=>surface.Style(m=>m.RemoveWhite=!m.RemoveWhite),active:current.RemoveWhite);
                    RailIcon(g,R(25,157,37,31),"logo","Дублировать",DuplicateSelected,false);
                    RailIcon(g,R(69,157,37,31),"trash","Удалить",surface.Delete,false);
                }
                return;
            }
            bool outline=Surface.Outline(kind), note=kind=="note", graph=kind=="graph";
            if(!outline&&!note&&!graph&&current==null) return;
            var box=R(14,73,219,outline?Surface.Shape(kind)?312:254:current!=null?note||graph?159:99:105);
            if(popup=="background"||popup=="main"||popup=="boards") return;
            PopupBase(g,box,current==null?"Настройки инструмента":"Свойства объекта");
            float y=box.Y+37;
            if(outline||note||graph) {
                string value=note?(current==null?surface.NoteColor:current.Color):(current==null?surface.Color:current.Color);
                value=value??"#5F43CD";
                Palette(g,box.X,y,note?"Цвет заметки":"Обводка",note?NoteFills:swatches,value,chosen=>{
                    if(note&&String.IsNullOrEmpty(chosen)) return;
                    if(current!=null) surface.Style(m=>m.Color=chosen);
                    else if(note) surface.NoteColor=chosen;
                    else surface.Color=chosen;
                });
                y+=58;
            }
            if(Surface.Shape(kind)) {
                Palette(g,box.X,y,"Заливка",Fills,current==null?surface.Fill:current.Fill??"",chosen=>{
                    if(current!=null) surface.Style(m=>m.Fill=chosen);else surface.Fill=chosen;
                });
                y+=58;
            }
            if(outline) {
                SettingLabel(g,"Толщина",box.X,y);
                var weights=new[]{1.5f,2.5f,5f,9f};
                for(int i=0;i<weights.Length;i++) {
                    float weight=weights[i];var r=R(box.X+12+i*48,y+22,43,27);
                    OptionBase(g,r,Math.Abs((current==null?surface.Weight:current.Size)-weight)<.2);
                    Art.Stroke(g,Art.Ink,weight,r.X+10,r.Y+13,r.Right-10,r.Y+13);
                    Target(r,()=>{if(current!=null)surface.Style(m=>m.Size=weight);else surface.Weight=weight;},"Толщина "+weight);
                }
                y+=57;
                SettingLabel(g,"Тип штриха",box.X,y);
                string[] styles={"","dash","dot"},names={"Сплошной","Штрихи","Точки"};
                for(int i=0;i<3;i++) {
                    string style=styles[i];var r=R(box.X+12+i*65,y+21,60,24);
                    OptionBase(g,r,(current==null?surface.Dash:current.Dash??"")==style);
                    using(var pen=Art.Pen(Art.Ink,2)) {
                        if(style=="dash")pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;
                        if(style=="dot")pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dot;
                        g.DrawLine(pen,r.X+12,r.Y+12,r.Right-12,r.Y+12);
                    }
                    Target(r,()=>{if(current!=null)surface.Style(m=>m.Dash=style);else surface.Dash=style;},names[i]);
                }
                y+=50;
                int opacity=current==null?surface.Opacity:current.Opacity??100;
                SettingLabel(g,"Непрозрачность   "+opacity+"%",box.X,y);
                int[] levels={25,50,75,100};
                for(int i=0;i<levels.Length;i++) {
                    int level=levels[i];var r=R(box.X+12+i*48,y+21,43,25);
                    OptionBase(g,r,opacity==level);
                    Art.Label(g,level+"%",r.X,r.Y,r.Width,r.Height,11,Art.Ink,false,StringAlignment.Center);
                    Target(r,()=>{if(current!=null)surface.Style(m=>m.Opacity=level);else surface.Opacity=level;},"Непрозрачность "+level+"%");
                }
                y+=54;
            }
            if(current!=null) {
                bool editable=kind=="heading"||kind=="text"||kind=="note"||kind=="link"||kind=="graph"||kind=="trig"||kind=="frame"||kind=="task";
                var actions=R(box.X,y-1,box.Width,43);
                if(outline) {Art.Round(g,actions,11,Art.White,Art.Line);Target(actions,()=>{});}
                if(editable) RailIcon(g,R(box.X+12,y,37,34),"edit","Редактировать",()=>EditMark(surface.Selected),false);
                RailIcon(g,R(box.X+55,y,37,34),"logo","Дублировать · Ctrl+D",DuplicateSelected,false);
                RailIcon(g,R(box.X+97,y,37,34),"trash","Удалить · Del",surface.Delete,false);
            }
        }
        void DrawDiagramOptions(Graphics g,Mark item) {
            bool circuit=DiagramEdit.IsCircuit(item),points=DiagramEdit.HasPoints(item),measure=DiagramEdit.HasMeasurements(item);
            var box=R(14,73,219,circuit?199:214);
            PopupBase(g,box,circuit?"Электрическая цепь":"Чертёж");
            Art.Label(g,points?"Перетащите вершину":item.Title,box.X+12,box.Y+35,195,22,12,Art.Muted);
            if(!circuit)Palette(g,box.X,box.Y+62,"Линии",DiagramSwatches,String.IsNullOrEmpty(item.Color)?"#253345":item.Color,
                chosen=>surface.Style(m=>m.Color=chosen));
            float y=box.Y+(circuit?60:121);
            Button(g,R(box.X+11,y,96,30),null,"Подписи",()=>surface.Style(m=>m.Labels=!(m.Labels??true)),active:item.Labels??true);
            if(measure)Button(g,R(box.X+111,y,97,30),null,"Замеры",()=>surface.Style(m=>m.Measurements=!m.Measurements),active:item.Measurements);
            Button(g,R(box.X+11,y+42,circuit?196:119,32),"edit",circuit?"Компоненты":"Правка",()=>EditMark(surface.Selected));
            if(!circuit) {
                RailIcon(g,R(box.X+130,y+41,37,34),"logo","Дублировать",DuplicateSelected,false);
                RailIcon(g,R(box.X+171,y+41,37,34),"trash","Удалить",surface.Delete,false);
            } else {
                RailIcon(g,R(box.X+11,y+81,37,34),"logo","Дублировать",DuplicateSelected,false);
                RailIcon(g,R(box.X+56,y+81,37,34),"trash","Удалить",surface.Delete,false);
            }
        }
        static readonly string[] DiagramSwatches={"#253345","#5F43CD","#167F76","#2766AC","#BA3E60","#C56A1B"};
        void DuplicateSelected() {
            if(surface.Selected==null) return;
            var next=surface.Selected.Copy();next.X+=24;next.Y+=24;surface.Add(next);
        }
        void SettingLabel(Graphics g,string title,float x,float y) {Art.Label(g,title,x+12,y,192,20,12,Art.Muted);}
        void OptionBase(Graphics g,RectangleF r,bool active) { Art.Round(g,r,7,active?Art.Pale:Hover(r)?Art.Sidebar:Art.C("#F7F6FA")); }
        void Palette(Graphics g,float x,float y,string title,string[] colors,string value,Action<string> use) {
            SettingLabel(g,title,x,y);
            for(int i=0;i<colors.Length;i++) {
                string color=colors[i];var r=R(x+12+i*32,y+23,26,26);
                Art.Round(g,r,7,Hover(r)?Art.Pale:Art.White,color==value?Art.Purple:Art.Line);
                var inner=R(r.X+3,r.Y+3,20,20);
                if(String.IsNullOrEmpty(color)) {
                    Art.Round(g,inner,5,Art.C("#F1EEF6"));
                    Art.Stroke(g,Art.Fade,1.4f,r.X+7,r.Bottom-7,r.Right-7,r.Y+7);
                } else Art.Round(g,inner,5,Art.ColorOf(color,Art.Purple));
                Target(r,()=>use(color),String.IsNullOrEmpty(color)?"Прозрачный":color);
            }
        }
        void DrawToolPopup(Graphics g) {
            if(popup=="more") {
                var more=R(strip.Right-275,strip.Bottom+7,275,155);
                PopupBase(g,more,"Дополнительные инструменты");
                PopupRow(g,R(more.X+7,more.Y+38,261,33),"link","Ссылка · K",()=>ChooseTool("link"));
                PopupRow(g,R(more.X+7,more.Y+74,261,33),"spark","Ровные фигуры  "+(surface.Tidy?"✓":"○"),ToggleTidy,true);
                PopupRow(g,R(more.X+7,more.Y+110,261,33),"image","Вставить изображение",AddWorksheet);
                return;
            }
            if(popup!="background"&&popup!="main"&&popup!="boards") return;
            var box=R(14,60,258,166);
            PopupBase(g,box,"Фон доски");
            Palette(g,box.X,box.Y+35,"Цвет фона",new[]{"#FFFFFF","#F8F9FB","#FFF8E7","#EDF7F3","#EBF3FB","#F3ECFA"},
                surface.Board.Background??"#FFFFFF",chosen=>{surface.Board.Background=chosen;MarkSettings();});
            SettingLabel(g,"Разметка",box.X,box.Y+93);
            string[] ids={"none","dots","grid","ruled"},names={"Чистый","Точки","Клетка","Линии"};
            for(int i=0;i<4;i++) {
                string id=ids[i];var option=R(box.X+7+i*61,box.Y+120,59,33);
                OptionBase(g,option,(surface.Board.Pattern??"grid")==id);
                Art.Label(g,names[i],option.X+2,option.Y,option.Width-4,option.Height,12,Art.Ink,true,StringAlignment.Center);
                Target(option,()=>{surface.Board.Pattern=id;MarkSettings();},names[i]);
            }
        }
    }
}
