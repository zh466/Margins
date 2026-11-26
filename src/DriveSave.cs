using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Polya {
    sealed partial class Workspace {
        public sealed class DriveOptions {
            public string Folder {get;set;}
            public bool Automatic {get;set;}
        }
        sealed class DriveWrite {
            public Notebook Snapshot;
            public string Title,Folder;
            public bool ShowResult;
        }
        DriveOptions driveOptions=new DriveOptions();
        readonly Dictionary<string,DriveWrite> pendingDrive=new Dictionary<string,DriveWrite>();
        bool closeWhenDriveDone;
        Task driveWriter;
        static string DriveOptionsPath {get{return Path.Combine(Path.GetDirectoryName(RecoveryPath),"drive.json");}}
        void LoadDriveOptions() {
            if(!recoveryAllowed||!File.Exists(DriveOptionsPath))return;
            try {
                driveOptions=new JavaScriptSerializer().Deserialize<DriveOptions>(File.ReadAllText(DriveOptionsPath))??new DriveOptions();
            } catch(Exception) {driveOptions=new DriveOptions();}
        }
        internal static string CloudFile(string folder,Notebook snapshot,string title) {
            var bad=Path.GetInvalidFileNameChars();
            string name=new string((title??"Тетрадь").Where(c=>Array.IndexOf(bad,c)<0).Take(72).ToArray()).Trim(' ','.');
            if(name.Length==0)name="Тетрадь";
            // A stable notebook key keeps identical names from overwriting each other.
            var id=snapshot.GetId();
            var existing=Directory.EnumerateFiles(folder,"* ["+id+"].polya").FirstOrDefault();
            return existing??Path.Combine(folder,name+" ["+id+"].polya");
        }
        void ConfigureDrive(bool saveNow) {
            var sheet=new Sheet("Google Диск","Выберите папку, которую синхронизирует Google Диск для компьютеров",610,294);
            var folder=sheet.Field(driveOptions.Folder,22,120,435);
            sheet.Action("Выбрать…",465,118,124,()=>{
                using(var picker=new FolderBrowserDialog {
                    Description="Папка Google Диска для тетрадей",SelectedPath=folder.Text,ShowNewFolderButton=true
                }) if(picker.ShowDialog(this)==DialogResult.OK)folder.Text=picker.SelectedPath;
            });
            folder.AccessibleName="Папка Google Диска";
            var auto=sheet.Put(new CheckBox{Text="Автоматически сохранять тетради в эту папку",Checked=driveOptions.Automatic},22,171,565,25);
            DriveOptions next=null;
            sheet.Footer(saveNow?"Сохранить сейчас":"Запомнить",()=>{
                try {
                    string value=Environment.ExpandEnvironmentVariables(folder.Text.Trim().Trim('"'));
                    if(value.Length>0) {
                        value=Path.GetFullPath(value);
                        if(!Directory.Exists(value))return sheet.Error("Папка недоступна. Проверьте подключение Google Диска.");
                    } else if(saveNow||auto.Checked)return sheet.Error("Укажите папку Google Диска.");
                    next=new DriveOptions{Folder=value,Automatic=auto.Checked};
                    if(recoveryAllowed) {
                        Directory.CreateDirectory(Path.GetDirectoryName(DriveOptionsPath));
                        File.WriteAllText(DriveOptionsPath,new JavaScriptSerializer().Serialize(next));
                    }
                    return true;
                } catch(Exception ex){return sheet.Error(ex.Message);}
            });
            sheet.OnBoard(this,result=>{
                if(result!=DialogResult.OK)return;
                driveOptions=next;
                if(saveNow){FinishText(true);SendToDrive(Snapshot(),true);}
                else if(next.Automatic)RecordSession();
            });
        }
        void Drive() {
            if(String.IsNullOrWhiteSpace(driveOptions.Folder)){ConfigureDrive(true);return;}
            FinishText(true);
            SendToDrive(Snapshot(),true);
        }
        void SendToDrive(Notebook snapshot,bool showResult) {
            string folder=driveOptions.Folder;
            if(String.IsNullOrWhiteSpace(folder))return;
            var title=String.IsNullOrEmpty(path)?snapshot.Boards[snapshot.Active].Name:Path.GetFileNameWithoutExtension(path);
            string key=folder+"|"+snapshot.GetId();
            DriveWrite previous;
            pendingDrive.TryGetValue(key,out previous);
            pendingDrive[key]=new DriveWrite{Snapshot=snapshot,Title=title,Folder=folder,
                ShowResult=showResult||(previous!=null&&previous.ShowResult)};
            if(showResult)Notice("Запись в папку Google Диска…");
            StartDriveWriter();
        }
        void StartDriveWriter() {
            if(driveWriter!=null||pendingDrive.Count==0)return;
            var next=pendingDrive.First();
            var job=next.Value;
            pendingDrive.Remove(next.Key);
            var folder=job.Folder;
            driveWriter=Task.Run(()=>{
                string message=null;
                try {
                    if(!Directory.Exists(folder))throw new IOException("Папка сейчас недоступна.");
                    var destination=CloudFile(folder,job.Snapshot,job.Title);
                    job.Snapshot.Write(destination);
                    if(job.ShowResult)message="Файл записан в папку Google Диска.";
                } catch(Exception ex) {message="Google Диск: "+ex.Message;}
                if(IsDisposed||!IsHandleCreated)return;
                try { BeginInvoke(new Action(()=>{
                    driveWriter=null;
                    if(message!=null)Notice(message);
                    StartDriveWriter();
                    if(closeWhenDriveDone&&driveWriter==null)Close();
                })); } catch(InvalidOperationException) {}
            });
        }
        internal static void WriteRecovery(Notebook snapshot,string session) {
            string directory=Path.GetDirectoryName(session);
            Directory.CreateDirectory(directory);
            string encoded=snapshot.Encode();
            Notebook.WriteEncoded(session,encoded);
            var notebooks=Path.Combine(directory,"Тетради");
            Directory.CreateDirectory(notebooks);
            Notebook.WriteEncoded(Path.Combine(notebooks,snapshot.GetId()+".polya"),encoded);
        }
    }
}
