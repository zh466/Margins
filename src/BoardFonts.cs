using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Polya {
    static class BoardFonts {
        sealed class ResourceFace {
            readonly PrivateFontCollection collection;
            public readonly FontFamily Family;
            public ResourceFace(string file) {
                using(var data=Assembly.GetExecutingAssembly().GetManifestResourceStream("Polya.Font."+file)) {
                    if(data==null)return;
                    var bytes=new byte[data.Length];
                    int length=0;
                    while(length<bytes.Length) {
                        int count=data.Read(bytes,length,bytes.Length-length);
                        if(count==0)return;
                        length+=count;
                    }
                    // GDI+ needs the font buffer for the lifetime of the collection.
                    var buffer=Marshal.AllocCoTaskMem(bytes.Length);
                    try {
                        Marshal.Copy(bytes,0,buffer,bytes.Length);
                        collection=new PrivateFontCollection();collection.AddMemoryFont(buffer,bytes.Length);
                        var families=collection.Families;
                        if(families.Length>0)Family=families[0];
                    } catch(ArgumentException) {Marshal.FreeCoTaskMem(buffer);}
                }
            }
        }
        static readonly ResourceFace Latin=new ResourceFace("Assistant-Regular.ttf"),
            LatinStrong=new ResourceFace("Assistant-SemiBold.ttf"),
            Cyrillic=new ResourceFace("SourceSans3-Regular.ttf"),CyrillicStrong=new ResourceFace("SourceSans3-Semibold.ttf");
        public static bool IsCyrillic(string text) {
            if(String.IsNullOrEmpty(text))return true;
            foreach(char c in text) if(c>='\u0400'&&c<='\u052F')return true;
            return false;
        }
        public static Font Create(float size,bool bold,bool cyrillic) {
            var face=cyrillic?(bold?CyrillicStrong:Cyrillic):(bold?LatinStrong:Latin);
            if(face.Family!=null) {
                var style=face.Family.IsStyleAvailable(FontStyle.Regular)?FontStyle.Regular:FontStyle.Bold;
                return new Font(face.Family,size,style,GraphicsUnit.Pixel);
            }
            return new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
        }
    }
}
