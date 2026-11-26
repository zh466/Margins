using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Polya {
    sealed partial class Workspace {
        enum ScreenPage { Subjects, Files, Board }
        ScreenPage page;
        PointF contextAt;
        string librarySubject="math",popup;
        int fileOffset,boardOffset;
        public class RecentFile { public string Path {get;set;} public string[] Subjects {get;set;} public DateTime? Opened {get;set;} }
        List<RecentFile> recent=new List<RecentFile>();
        static string RecentPath { get {return Path.Combine(Path.GetDirectoryName(RecoveryPath),"recent.json");} }
        void LoadRecent() {
            if(!recoveryAllowed) return;
            try {
                if(File.Exists(RecentPath)) recent=new JavaScriptSerializer().Deserialize<List<RecentFile>>(File.ReadAllText(RecentPath))
                    .Where(f=>f!=null&&!String.IsNullOrWhiteSpace(f.Path)&&f.Subjects!=null).Take(40).ToList();
            } catch(Exception) { recent.Clear(); }
        }
        void Remember(string source) {
            recent.RemoveAll(f=>String.Equals(source,f.Path,StringComparison.OrdinalIgnoreCase));
            recent.Insert(0,new RecentFile{Path=source,Opened=DateTime.UtcNow,Subjects=doc.Boards.Where(b=>b==surface.Board||b.Marks.Count>0).Select(b=>b.Subject).Distinct().ToArray()});
            if(recent.Count>40) recent.RemoveAt(40);
            if(!recoveryAllowed) return;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentPath));
                File.WriteAllText(RecentPath,new JavaScriptSerializer().Serialize(recent));
            } catch(Exception) { Notice("Не удалось обновить список недавних файлов"); }
        }
        void Navigate(ScreenPage next) {
            StopZoom();CloseCanvasPanel();CloseBrowser();
            page=next;popup=null;fileOffset=0;
            Invalidate();
        }
        void TogglePopup(string name) {
            popup=popup==name?null:name;
            if(popup=="boards") boardOffset=Math.Max(0,doc.Active-3);
            Invalidate();
        }
        void MarkSettings() {dirty=true;RecordSession();Invalidate();}
        void OpenRecent(RecentFile item) {
            if(!File.Exists(item.Path)) {
                Prompts.Alert(this,"Файл недоступен","Файл мог быть перемещён или диск сейчас отключён. Выберите его через «Открыть файл».");
                return;
            }
            if(CheckUnsaved()) LoadNotebook(item.Path);
        }
        void NewForSubject(bool example) {
            if(!CheckUnsaved()) return;
            var next=Seed.Create(example);next.Active=Array.IndexOf(Seed.Subjects,librarySubject);
            SetNotebook(next,null); if(example) {firstLayout=true; ViewChanged();}
        }
        void DrawBoardShell(Graphics g) {
            DrawBoardControls(g);
            DrawToolStrip(g);
            float zx=18,zy=screenH-54;
            Art.Round(g,R(zx,zy,170,36),10,Art.White,Art.Line);
            BottomButton(g,R(zx+3,zy+2,32,32),"minus",null,()=>Zoom(1/1.2f),"Уменьшить");
            BottomButton(g,R(zx+35,zy+2,60,32),null,Math.Round(surface.Board.Zoom*100)+"%",()=>{surface.ResetZoom();ViewChanged();},"Масштаб 100%");
            BottomButton(g,R(zx+95,zy+2,32,32),"plus",null,()=>Zoom(1.2f),"Увеличить");
            BottomButton(g,R(zx+133,zy+2,30,32),"fit",null,()=>{surface.Fit();ViewChanged();},"Показать всю доску");
            Art.Round(g,R(zx+180,zy,80,36),10,Art.White,Art.Line);
            RailIcon(g,R(zx+183,zy+1,36,34),"undo","Отменить · Ctrl+Z",()=>surface.Undo(false),false,!surface.CanUndo);
            RailIcon(g,R(zx+221,zy+1,36,34),"redo","Повторить · Ctrl+Y",()=>surface.Undo(true),false,!surface.CanRedo);
            Button(g,R(screenW-123,zy,36,36),"help",null,Help,"Горячие клавиши · F1");
            if(status!="Готово к работе") Art.Label(g,status,screenW/2-120,zy,240,36,12,Art.Muted,false,StringAlignment.Center);
            if(Surface.IsEraser(surface.Tool)&&pointerSeen&&surface.Area.Contains(pointer)&&At(pointer)==null&&popup==null) {
                Art.Dot(g,Color.FromArgb(125,255,255,255),pointer.X,pointer.Y,10);
                Art.Circle(g,Art.Muted,pointer.X,pointer.Y,10,1.15f);
            }
            DrawToolOptions(g);

            DrawLessonClock(g);
            DrawPopup(g);
        }
        void RailIcon(Graphics g,RectangleF r,string icon,string tip,Action run,bool active,bool disabled=false) {
            if(active||Hover(r)&&!disabled) Art.Round(g,r,9,active?Art.Pale:Art.Sidebar);
            Art.Icon(g,icon,r.X+8,r.Y+6,21,active?Art.Purple:disabled?Art.Fade:Art.Ink);
            if(!disabled) Target(r,run,tip);
        }
        void DrawStudyMenu(Graphics g) {
            var tools=Catalog.Tools(surface.Board.Subject);
            var r=R(screenW-279,60,265,44+tools.Length*36);
            PopupBase(g,r,"Учебные инструменты");
            for(int i=0;i<tools.Length;i++) {
                var tool=tools[i];
                PopupRow(g,R(r.X+7,r.Y+38+i*36,r.Width-14,34),tool.Icon,tool.Name,()=>UseStudy(tool));
            }
        }
        void UseStudy(Catalog.Group tool) {
            if(tool.Id=="calc") {Calculator();return;}
            if(tool.Id=="book") {Reference();return;}
            if(tool.Id=="graph") {NewWidget("graph");return;}
            Catalog.Pick(this,tool,InsertStudyItem);
        }
        void InsertStudyItem(Catalog.Item item) {
            if(item==null) return;
            if(item.Id=="graph"||item.Id=="trig") {NewWidget(item.Id);return;}
            var center=surface.Center();
            surface.Add(Mark.Make("diagram",center.X-190,center.Y-135,380,270,item.Name,item.Id));
            ChooseTool("select");
        }
        void PopupBase(Graphics g,RectangleF r,string title) {
            Art.Round(g,R(r.X+1,r.Y+5,r.Width,r.Height),13,Color.FromArgb(25,37,20,70));
            Art.Round(g,r,13,Art.White,Art.Line);
            Art.Label(g,title,r.X+14,r.Y+7,r.Width-28,29,14,Art.Ink,true);
            Target(r,()=>{});
        }
        void PopupRow(Graphics g,RectangleF r,string icon,string name,Action run,bool keep=false) {
            if(Hover(r)) Art.Round(g,r,8,Art.Pale);
            Art.Icon(g,icon,r.X+7,r.Y+6,19,Art.Purple);
            Art.Label(g,name,r.X+35,r.Y,r.Width-45,r.Height,13,Art.Ink);
            Target(r,()=>{if(!keep)popup=null;run();},name);
        }
        void DrawPopup(Graphics g) {
            if(popup==null) return;
            if(popup=="main") {DrawBoardMenu(g);return;}
            if(popup=="lesson") {DrawLessonPopup(g);return;}
            if(popup=="study") {DrawStudyMenu(g);return;}
            if(popup=="boards") {
                int rows=Math.Min(8,doc.Boards.Count);boardOffset=Math.Max(0,Math.Min(boardOffset,doc.Boards.Count-rows));
                var r=R(screenW-314,60,300,150+rows*33);PopupBase(g,r,"Доски тетради");
                for(int i=0;i<rows;i++) {int index=i+boardOffset;var b=doc.Boards[index];
                    PopupRow(g,R(r.X+8,r.Y+37+i*33,284,31),index==doc.Active?"check":b.Subject,b.Name,()=>SelectBoard(index));
                }
                if(doc.Boards.Count>rows) {
                    Button(g,R(r.Right-76,r.Y+7,28,26),"up",null,()=>boardOffset--,"Предыдущие доски");
                    Button(g,R(r.Right-38,r.Y+7,28,26),"down",null,()=>boardOffset++,"Следующие доски");
                }
                float bottom=r.Y+38+rows*33;
                PopupRow(g,R(r.X+8,bottom,284,31),"plus","Добавить доску",AddBoard);
                PopupRow(g,R(r.X+8,bottom+33,284,31),"edit","Переименовать",RenameBoard);
                PopupRow(g,R(r.X+8,bottom+66,284,31),"trash","Удалить текущую доску",DeleteBoard);
                return;
            }
            if(popup=="canvas") {
                var r=R(Math.Min(contextAt.X,screenW-240),Math.Min(contextAt.Y,screenH-146),225,142);
                PopupBase(g,r,"Холст");
                PopupRow(g,R(r.X+7,r.Y+36,211,32),"fit","Доска целиком",()=>{surface.Fit();ViewChanged();});
                PopupRow(g,R(r.X+7,r.Y+69,211,32),"background","Настройки фона",()=>TogglePopup("background"));
                PopupRow(g,R(r.X+7,r.Y+102,211,32),"spark","Ровные фигуры "+(surface.Tidy?"✓":""),ToggleTidy,true);
                return;
            }
            DrawToolPopup(g);
        }
    }
}
