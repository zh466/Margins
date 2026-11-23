using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Polya {
    sealed partial class Workspace {
        const float LibraryRail=188;
        void OpenSubject(string subject) {librarySubject=subject;Navigate(ScreenPage.Files);}
        RecentFile[] VisibleFiles() {
            return page==ScreenPage.Subjects?recent.ToArray():recent.Where(f=>f.Subjects.Contains(librarySubject)).ToArray();
        }
        int VisibleFileCount() {return Math.Max(1,(int)((screenH-418)/53));}
        void ScrollLibrary(int delta) {
            fileOffset=Math.Max(0,Math.Min(Math.Max(0,VisibleFiles().Length-VisibleFileCount()),fileOffset-Math.Sign(delta)));
            Invalidate();
        }
        void DrawLibrary(Graphics g) {
            Art.Fill(g,Art.White,R(0,0,screenW,screenH));
            Art.Round(g,R(LibraryRail,10,screenW-LibraryRail-10,screenH-20),17,Art.C("#F8F9FB"),Art.Line);
            Art.Round(g,R(19,22,34,34),9,Art.Dark);
            Art.Icon(g,"logo",25,28,22,Art.White);
            Art.Label(g,"Поля",62,21,84,35,22,Art.Ink,true);
            LibraryNav(g,77,"other","Все предметы",page==ScreenPage.Subjects,()=>Navigate(ScreenPage.Subjects));
            LibraryNav(g,120,"open","Открыть файл",false,OpenNotebook);
            Art.Stroke(g,Art.Line,1,17,177,LibraryRail-17,177);
            Art.Label(g,"ПРЕДМЕТЫ",20,190,145,23,10,Art.Muted,true);
            for(int i=0;i<Seed.Subjects.Length;i++) {
                string subject=Seed.Subjects[i];
                LibraryNav(g,222+i*42,subject,Seed.Subject(subject),page==ScreenPage.Files&&subject==librarySubject,()=>OpenSubject(subject));
            }
            LibraryNav(g,screenH-64,"arrow","К текущей доске",false,()=>Navigate(ScreenPage.Board));
            float right=screenW-26;
            Button(g,R(right-131,27,131,34),"open","Открыть",OpenNotebook,"Открыть файл · Ctrl+O");
            float w=Math.Min(1088,screenW-LibraryRail-64);
            float x=LibraryRail+(screenW-LibraryRail-w)/2;
            if(page==ScreenPage.Subjects) {
                Art.Label(g,"Предметы",x,67,w,63,42,Art.Ink,true,StringAlignment.Center);
                Art.Round(g,R(x,151,w,199),14,Art.White,Art.Line);
                Art.Label(g,"Выбрать предмет",x+18,159,w-36,30,16,Art.Ink,true);
                float step=(w-34)/4;
                for(int i=0;i<Seed.Subjects.Length;i++) {
                    string subject=Seed.Subjects[i];
                    var r=R(x+17+i*step,197,step-10,132);
                    SubjectTile(g,r,subject,Seed.Subject(subject),()=>OpenSubject(subject));
                }
            } else {
                Art.Icon(g,librarySubject,x+4,79,34,SubjectFg(librarySubject));
                Art.Label(g,Seed.Subject(librarySubject),x+52,64,w-60,63,35,Art.Ink,true);
                Art.Round(g,R(x,151,w,199),14,Art.White,Art.Line);
                Art.Label(g,"Создать или открыть",x+18,159,w-36,30,16,Art.Ink,true);
                float step=(w-34)/3;
                LibraryTile(g,R(x+17,197,step-10,132),"plus","Новая тетрадь",()=>NewForSubject(false));
                SubjectTile(g,R(x+17+step,197,step-10,132),librarySubject,"Учебный пример",()=>NewForSubject(true));
                LibraryTile(g,R(x+17+2*step,197,step-10,132),"open","Файл с компьютера",OpenNotebook);
            }
            DrawRecents(g,x,w);
        }
        void LibraryNav(Graphics g,float y,string icon,string title,bool selected,Action action) {
            var row=R(11,y,LibraryRail-22,37);
            if(selected||Hover(row)) Art.Round(g,row,8,selected?Art.Pale:Art.Sidebar);
            Art.Icon(g,icon,row.X+9,row.Y+9,19,selected?Art.Purple:Art.Muted);
            Art.Label(g,title,row.X+36,row.Y,row.Width-42,row.Height,13,selected?Art.Purple:Art.Ink,selected);
            Target(row,action,title);
        }
        void LibraryTile(Graphics g,RectangleF r,string icon,string title,Action action) {
            var thumbnail=R(r.X+1,r.Y+1,r.Width-2,98);
            Art.Round(g,thumbnail,9,Hover(r)?Art.Pale:Art.C("#F3F4F7"),Art.Line);
            Art.Icon(g,icon,r.X+(r.Width-29)/2,r.Y+36,29,Art.Purple);
            Art.Label(g,title,r.X+2,r.Y+103,r.Width-4,23,13,Art.Ink,true);
            Target(r,action,title);
        }
        void SubjectTile(Graphics g,RectangleF r,string subject,string title,Action action) {
            var thumbnail=R(r.X+1,r.Y+1,r.Width-2,98);
            Art.Round(g,thumbnail,9,SubjectBg(subject),Hover(r)?SubjectFg(subject):Art.Line);
            var art=R(thumbnail.X+10,thumbnail.Y+8,thumbnail.Width-20,thumbnail.Height-16);
            if(subject=="math"||subject=="physics") {
                var sample=Mark.Make("diagram",art.X,art.Y,art.Width,art.Height,"",subject=="math"?"altitude":"circuit");
                sample.Labels=false;
                DiagramEdit.Draw(g,sample);
            } else if(subject=="russian") {
                Art.Label(g,"[ … ], и [ … ].",art.X,art.Y,art.Width,art.Height,Math.Min(24,art.Width/6),SubjectFg(subject),true,StringAlignment.Center);
            } else {
                float cx=art.X+art.Width/2,cy=art.Y+art.Height/2;
                Art.Round(g,R(cx-39,cy-19,42,38),5,Art.White);
                Art.Round(g,R(cx-8,cy-10,44,36),5,Art.C("#FFF0BE"));
                Art.Stroke(g,SubjectFg(subject),1.4f,cx-27,cy-7,cx-9,cy-7);
                Art.Stroke(g,SubjectFg(subject),1.4f,cx+3,cy+4,cx+25,cy+4);
            }
            Art.Label(g,title,r.X+2,r.Y+103,r.Width-4,23,13,Art.Ink,true);
            Target(r,action,title);
        }
        void DrawRecents(Graphics g,float x,float width) {
            var files=VisibleFiles();
            Art.Label(g,"Недавние тетради",x+2,366,width-4,33,20,Art.Ink,true);
            if(files.Length==0) {
                Art.Label(g,"Здесь появятся открытые и сохранённые файлы .polya.",x+2,410,width-4,26,14,Art.Muted);
                return;
            }
            float subjectX=x+width*.59f,dateX=x+width*.82f;
            Art.Label(g,"Название",x+10,402,width*.52f,21,11,Art.Muted);
            Art.Label(g,"Предмет",subjectX,402,width*.20f,21,11,Art.Muted);
            Art.Label(g,"Открывалась",dateX,402,width*.17f,21,11,Art.Muted);
            int count=VisibleFileCount();
            fileOffset=Math.Max(0,Math.Min(fileOffset,Math.Max(0,files.Length-count)));
            for(int i=0;i<count&&i+fileOffset<files.Length;i++) {
                var file=files[i+fileOffset];float y=425+i*53;
                var r=R(x,y,width,52);
                string subject=page==ScreenPage.Files?librarySubject:file.Subjects.FirstOrDefault()??"other";
                if(Hover(r))Art.Round(g,r,7,Art.White);
                Art.Stroke(g,Art.Line,1,x,y+51,x+width,y+51);
                Art.Round(g,R(x+9,y+12,29,29),7,SubjectBg(subject));
                Art.Icon(g,"other",x+15,y+18,17,SubjectFg(subject));
                Art.Label(g,Path.GetFileNameWithoutExtension(file.Path),x+47,y+4,width*.55f-48,23,14,Art.Ink,true);
                Art.Label(g,Path.GetDirectoryName(file.Path),x+47,y+26,width*.55f-48,19,11,Art.Muted);
                Art.Label(g,Seed.Subject(subject),subjectX,y,width*.20f,52,12,Art.Muted);
                if(file.Opened.HasValue)Art.Label(g,file.Opened.Value.ToLocalTime().ToString("dd.MM.yyyy"),dateX,y,width*.17f,52,12,Art.Muted);
                Target(r,()=>OpenRecent(file),file.Path);
            }
        }
        void DrawBoardControls(Graphics g) {
            Button(g,R(14,16,38,38),"menu",null,()=>TogglePopup("main"),"Меню доски",active:popup=="main");
            float right=screenW-14;
            Button(g,R(right-38,16,38,38),"other",null,()=>TogglePopup("boards"),"Доски тетради",active:popup=="boards");
            if(screenW<1090) {
                Button(g,R(right-84,16,38,38),"save",null,()=>Save(false),"Сохранить · Ctrl+S",true);
                Button(g,R(right-130,16,38,38),"frame",null,()=>TogglePopup("lesson"),"Урок",active:popup=="lesson");
                return;
            }
            Button(g,R(right-166,16,120,38),"save","Сохранить",()=>Save(false),"Ctrl+S",true);
            Button(g,R(right-268,16,94,38),"frame","Урок",()=>TogglePopup("lesson"),"Кадры, задания и таймер",active:popup=="lesson");
        }
        void DrawBoardMenu(Graphics g) {
            var r=R(14,60,300,441);
            PopupBase(g,r,surface.Board.Name);
            PopupRow(g,R(21,99,286,34),"back","К предметам",()=>Navigate(ScreenPage.Subjects));
            Art.Stroke(g,Art.Line,1,24,140,304,140);
            PopupRow(g,R(21,147,286,34),"open","Открыть тетрадь",OpenNotebook);
            PopupRow(g,R(21,181,286,34),"plus","Новая тетрадь",NewNotebook);
            PopupRow(g,R(21,215,286,34),"save","Сохранить как…",()=>Save(true));
            PopupRow(g,R(21,249,286,34),"external","Сохранить в Google Drive",Drive);
            PopupRow(g,R(21,283,286,34),"help","Справка и горячие клавиши",Help);
            Art.Stroke(g,Art.Line,1,24,324,304,324);
            PopupRow(g,R(21,330,286,34),"settings","Фон и разметка",()=>TogglePopup("background"));
            Palette(g,14,382,"Цвет холста",new[]{"#FFFFFF","#F8F9FB","#FFF8E7","#EDF7F3","#EBF3FB","#F3ECFA"},
                surface.Board.Background??"#FFFFFF",chosen=>{surface.Board.Background=chosen;MarkSettings();});
            Art.Label(g,dirty?"Есть изменения":path==null?"Локальная доска":"Тетрадь сохранена",26,459,272,28,12,Art.Muted);
        }
    }
}
