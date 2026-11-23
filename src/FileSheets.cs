using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Polya {
    static class Prompts {
        public static DialogResult Alert(IWin32Window owner,string title,string text) {
            return Show(owner,title,text,new[]{"Понятно"},new[]{DialogResult.OK});
        }
        public static DialogResult Confirm(IWin32Window owner,string title,string text,string yes="Продолжить") {
            return Show(owner,title,text,new[]{"Отмена",yes},new[]{DialogResult.Cancel,DialogResult.Yes});
        }
        public static DialogResult Changes(IWin32Window owner) {
            return Show(owner,"Сохранить изменения?","В текущей тетради есть изменения. Можно записать их в файл .polya или продолжить без сохранения файла.",
                new[]{"Отмена","Без сохранения","Сохранить"},new[]{DialogResult.Cancel,DialogResult.No,DialogResult.Yes});
        }
        static DialogResult Show(IWin32Window owner,string title,string text,string[] choices,DialogResult[] results) {
            const int width=518,height=250,button=145;
            using(var sheet=new Sheet(title,"Поля",width,height)) {
                var body=sheet.Body(text,24,98,width-48,87,true);
                body.BackColor=Art.White;body.BorderStyle=BorderStyle.None;body.TabStop=false;
                for(int i=0;i<choices.Length;i++) {
                    var result=results[i];
                    var action=sheet.Action(choices[i],width-20-(choices.Length-i)*(button+10),height-53,button,
                        ()=>sheet.DialogResult=result,i==choices.Length-1);
                    if(i==choices.Length-1) sheet.AcceptButton=action;
                    if(result==DialogResult.Cancel) sheet.CancelButton=action;
                }
                return owner==null?sheet.ShowDialog():sheet.ShowDialog(owner);
            }
        }
    }

    sealed class FileSheet : Sheet {
        sealed class Entry {
            public readonly string Path,Name;
            public readonly bool Folder;
            public Entry(string path,bool folder) {Path=path;Folder=folder;Name=System.IO.Path.GetFileName(path.TrimEnd('\\','/'));if(Name.Length==0)Name=path;}
        }
        readonly bool save;
        readonly bool images;
        readonly TextBox address,name;
        readonly ListBox listing;
        readonly Label message;
        string directory;
        int revision;
        public string Chosen {get;private set;}
        FileSheet(bool saving,string initial,string suggested,bool images=false):base(images?"Вставить изображение":saving?"Сохранить тетрадь":"Открыть тетрадь",
            images?"Выберите файл PNG или JPEG":"Файлы .polya на компьютере и подключённых дисках",690,554) {
            save=saving;this.images=images;
            var documents=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if(String.IsNullOrEmpty(documents)||!Directory.Exists(documents)) documents=Environment.CurrentDirectory;
            Action("Документы",22,98,113,()=>Go(documents));
            Action("Рабочий стол",143,98,125,()=>Go(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
            Action("Диски",276,98,91,()=>Go(null));
            Action("Вверх",577,98,91,Up);
            address=Field("",22,145,526);
            var go=Action("Перейти",557,139,111,()=>Go(address.Text));
            address.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter) {go.PerformClick();e.SuppressKeyPress=true;}};
            listing=Put(new ListBox {BorderStyle=BorderStyle.None},22,187,646,228);
            listing.DrawMode=DrawMode.OwnerDrawFixed;listing.ItemHeight=32;listing.IntegralHeight=false;
            listing.DrawItem+=DrawEntry;
            listing.SelectedIndexChanged+=(s,e)=>{var row=listing.SelectedItem as Entry;if(row!=null&&!row.Folder)name.Text=row.Name;};
            listing.DoubleClick+=(s,e)=>ActivateEntry();
            listing.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter) {ActivateEntry();e.SuppressKeyPress=true;}};
            message=Caption("",22,424,645);message.AutoEllipsis=true;
            name=Field(save?suggested:"",22,453,646);
            name.AccessibleName="Имя файла";
            Footer(save?"Сохранить":"Открыть",Choose);
            Shown+=(s,e)=>Go(String.IsNullOrWhiteSpace(initial)?documents:initial);
        }
        void Up() {
            if(directory==null) return;
            var parent=Directory.GetParent(directory);
            Go(parent==null?null:parent.FullName);
        }
        void ActivateEntry() {
            var row=listing.SelectedItem as Entry;
            if(row==null)return;
            if(row.Folder) Go(row.Path);else AcceptButton.PerformClick();
        }
        async void Go(string value) {
            int task=++revision;
            string full=null;
            try {
                if(!String.IsNullOrWhiteSpace(value)) {
                    value=Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                    full=Path.GetFullPath(Path.IsPathRooted(value)||directory==null?value:Path.Combine(directory,value));
                }
                listing.Enabled=false;message.Text="Читаю папку…";
                var entries=await Task.Run(()=>Entries(full));
                if(IsDisposed||task!=revision)return;
                directory=full;address.Text=full??"";
                listing.BeginUpdate();
                try {listing.Items.Clear();listing.Items.AddRange(entries);}
                finally {listing.EndUpdate();}
                listing.Enabled=true;
                message.Text=entries.Length==0?"В папке нет подходящих файлов.":entries.Length>=1500?"Показаны первые 1 500 элементов.":"Имя файла или полный путь";
            } catch(Exception ex) {
                if(IsDisposed||task!=revision)return;
                message.Text=ex.Message;listing.Enabled=true;
            }
        }
        bool Matches(string file) {
            string ext=Path.GetExtension(file).ToLowerInvariant();
            return images?ext==".png"||ext==".jpg"||ext==".jpeg":ext==".polya";
        }
        Entry[] Entries(string location) {
            if(location==null) return Directory.GetLogicalDrives().Select(path=>new Entry(path,true)).ToArray();
            var dirs=Directory.EnumerateDirectories(location).Take(1000).OrderBy(Path.GetFileName,StringComparer.CurrentCultureIgnoreCase)
                .Select(path=>new Entry(path,true));
            var files=Directory.EnumerateFiles(location,images?"*":"*.polya").Where(Matches)
                .Take(1500).OrderBy(Path.GetFileName,StringComparer.CurrentCultureIgnoreCase).Select(path=>new Entry(path,false));
            return dirs.Concat(files).Take(1500).ToArray();
        }
        void DrawEntry(object sender,DrawItemEventArgs e) {
            if(e.Index<0)return;
            var row=(Entry)listing.Items[e.Index];
            var g=e.Graphics;Art.Prepare(g);
            Art.Fill(g,Art.White,e.Bounds);
            bool selected=(e.State&DrawItemState.Selected)!=0;
            var r=new RectangleF(e.Bounds.X+1,e.Bounds.Y+1,e.Bounds.Width-3,e.Bounds.Height-2);
            Art.Round(g,r,8,selected?Art.Pale:Art.C("#F7F6FA"));
            Art.Icon(g,row.Folder?"open":"other",r.X+10,r.Y+5,19,Art.Purple);
            Art.Label(g,row.Name,r.X+39,r.Y,r.Width-53,r.Height,13,Art.Ink,selected);
        }
        bool Choose() {
            try {
                string value=Environment.ExpandEnvironmentVariables(name.Text.Trim().Trim('"'));
                var row=listing.SelectedItem as Entry;
                if(value.Length==0&&row!=null&&row.Folder) {Go(row.Path);return false;}
                if(value.Length==0) {message.Text="Введите имя файла.";return false;}
                if(directory==null&&!Path.IsPathRooted(value)) {message.Text="Сначала выберите диск и папку.";return false;}
                string combined=Path.GetFullPath(Path.IsPathRooted(value)?value:Path.Combine(directory,value));
                if(Directory.Exists(combined)) {Go(combined);return false;}
                if(save&&!Path.GetExtension(combined).Equals(".polya",StringComparison.OrdinalIgnoreCase)) combined+=".polya";
                if(images&&!Matches(combined)) {message.Text="Выберите файл PNG или JPEG.";return false;}
                if(!save&&!File.Exists(combined)) {message.Text="Файл не найден.";return false;}
                if(save&&!Directory.Exists(Path.GetDirectoryName(combined))) {message.Text="Папка для сохранения недоступна.";return false;}
                if(save&&File.Exists(combined)&&Prompts.Confirm(this,"Заменить существующий файл?",Path.GetFileName(combined),"Заменить")!=DialogResult.Yes) return false;
                Chosen=combined;return true;
            } catch(Exception ex) {message.Text=ex.Message;return false;}
        }
        public static string Pick(IWin32Window owner,bool save,string initial,string suggested) {
            using(var sheet=new FileSheet(save,initial,suggested)) return sheet.ShowDialog(owner)==DialogResult.OK?sheet.Chosen:null;
        }
        public static string PickImage(IWin32Window owner) {
            using(var sheet=new FileSheet(false,null,null,true)) return sheet.ShowDialog(owner)==DialogResult.OK?sheet.Chosen:null;
        }
    }
}
