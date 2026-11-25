using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using System.Windows.Forms;

public class BalanceRow { public string Code, Name; public int Count; }
public static class Exchange {
    static readonly XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static XElement Read(ZipArchive z,string path){
        var entry=z.GetEntry(path);
        if(entry==null || entry.Length>32000000)throw new InvalidDataException("Некорректный раздел XLSX: "+path);
        using(var f=entry.Open())using(var xml=XmlReader.Create(f,new XmlReaderSettings{XmlResolver=null,DtdProcessing=DtdProcessing.Prohibit,MaxCharactersInDocument=32000000}))
            return XElement.Load(xml);
    }
    static string Target(string path){
        var u=new Uri(new Uri("https://xlsx.invalid/xl/workbook.xml"),path);
        if(u.Host!="xlsx.invalid")throw new InvalidDataException("Внешний лист Excel.");
        return Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/'));
    }
    public static List<BalanceRow> Parse(byte[] bytes){
        using(var stream=new MemoryStream(bytes))using(var z=new ZipArchive(stream,ZipArchiveMode.Read)){
            var wb=Read(z,"xl/workbook.xml");
            var tab=wb.Descendants(ns+"sheet").FirstOrDefault();if(tab==null)throw new InvalidDataException("Книга без листов.");
            string id=(string)tab.Attribute(XName.Get("id","http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
            var links=Read(z,"xl/_rels/workbook.xml.rels").Elements().ToList();
            var link=links.FirstOrDefault(x=>(string)x.Attribute("Id")==id);
            if(link==null)throw new InvalidDataException("Не найден первый лист.");
            var shared=links.FirstOrDefault(x=>((string)x.Attribute("Type")??"").EndsWith("/sharedStrings"));
            var strings=shared==null?new List<string>():Read(z,Target((string)shared.Attribute("Target"))).Elements(ns+"si").Select(Text).ToList();
            var rows=new List<BalanceRow>();string code=null,name=null,qty=null;bool header=false;
            var codes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var row in Read(z,Target((string)link.Attribute("Target"))).Descendants(ns+"row")){
                var cells=new Dictionary<string,string>();
                foreach(var c in row.Elements(ns+"c")){
                    string pos=(string)c.Attribute("r")??"", col=new string(pos.TakeWhile(Char.IsLetter).ToArray());
                    string type=(string)c.Attribute("t"),v=(string)c.Element(ns+"v")??"";
                    if(type=="s"){int i;if(!Int32.TryParse(v,out i)||i<0||i>=strings.Count)throw new InvalidDataException("Некорректная ячейка "+pos);v=strings[i];}
                    if(type=="inlineStr")v=Text(c);
                    if(v.Trim().Length>0)cells.Add(col,v.Trim());
                }
                if(cells.Count==0)continue;
                if(!header){
                    code=Column(cells,"Артикул");name=Column(cells,"Наименование");qty=Column(cells,"Остаток");header=true;continue;
                }
                string sku,label,num;decimal n;
                if(!cells.TryGetValue(code,out sku)||!cells.TryGetValue(name,out label)||!cells.TryGetValue(qty,out num)||
                    !Decimal.TryParse(num.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out n)||n<0||n>Int32.MaxValue||n!=Decimal.Truncate(n)||!codes.Add(sku))
                    throw new InvalidDataException("Проверьте артикул, название и целый остаток в строке "+(string)row.Attribute("r"));
                rows.Add(new BalanceRow{Code=sku,Name=label,Count=(int)n});
            }
            if(rows.Count==0)throw new InvalidDataException("В книге нет строк с остатками.");
            return rows;
        }
    }
    static string Text(XElement e){return String.Concat(e.Descendants(ns+"t").Select(x=>x.Value));}
    static string Column(Dictionary<string,string> cells,string title){
        var cols=cells.Where(x=>String.Equals(x.Value,title,StringComparison.OrdinalIgnoreCase)).ToList();
        if(cols.Count!=1)throw new InvalidDataException("Нужен один столбец «"+title+"» на первом листе.");
        return cols[0].Key;
    }
}
public partial class Shop {
    public string BalanceFile,BalanceHash,BalanceError;
    public DateTime ImportedAt,ImportedFileUtc;
    public int ImportedRows;
    public bool ImportBalances(string file){
        var stamp=File.GetLastWriteTimeUtc(file);
        byte[] bytes;
        using(var io=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){
            if(io.Length>32000000)throw new InvalidDataException("Книга превышает 32 МБ.");
            using(var m=new MemoryStream()){io.CopyTo(m);bytes=m.ToArray();}
        }
        string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes));
        if(hash==BalanceHash)return false;
        if(stamp<ImportedFileUtc)throw new InvalidDataException("Эта выгрузка старше последней принятой.");
        var rows=Exchange.Parse(bytes);
        var index=Stock.ToDictionary(p=>p.Code,StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows){
            Spare p;
            if(index.TryGetValue(row.Code,out p)&&row.Count<p.OnHand-Free(p))
                throw new InvalidDataException(row.Code+": остаток меньше действующего резерва. Сверьте выдачи.");
        }
        foreach(var row in rows){
            Spare p;
            if(!index.TryGetValue(row.Code,out p)){p=new Spare{Code=row.Code};Stock.Add(p);}
            p.Name=row.Name;
            if(p.HasSnapshot && p.PreviousBalance==row.Count)continue;
            int delta=row.Count-p.OnHand;
            if(delta>0)Receive(p,delta);else p.OnHand=row.Count;
            p.HasSnapshot=true;p.PreviousBalance=row.Count;
        }
        BalanceHash=hash; ImportedAt=DateTime.Now; ImportedRows=rows.Count;ImportedFileUtc=stamp;
        return true;
    }
    public bool PollBalances(){
        if(String.IsNullOrEmpty(BalanceFile))return false;
        try{bool changed=ImportBalances(BalanceFile);bool hadError=BalanceError!=null;BalanceError=null;return changed||hadError;}
        catch(Exception ex){
            if(!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is XmlException || ex is ArgumentException))throw;
            if(BalanceError==ex.Message)return false;BalanceError=ex.Message;return true;
        }
    }
}
partial class Desk {
    void BalanceControls(FlowLayoutPanel panel){
        panel.Controls.Add(LabelFor("Обмен с 1С",15));
        if(shop.ImportedAt!=DateTime.MinValue)panel.Controls.Add(LabelFor("Обновлено "+shop.ImportedAt.ToString("dd.MM HH:mm")+" · позиций "+shop.ImportedRows,10));
        if(!String.IsNullOrEmpty(shop.BalanceError))panel.Controls.Add(LabelFor(shop.BalanceError,10));
        if(Role!="Кладовщик")return;
        panel.Controls.Add(ButtonFor("Подключить Excel с остатками",()=>{
            using(var dlg=new OpenFileDialog{Filter="Книга Excel (*.xlsx)|*.xlsx"}){
                if(dlg.ShowDialog(this)!=DialogResult.OK)return;
                Commit(()=>{shop.ImportBalances(dlg.FileName);shop.BalanceFile=dlg.FileName;shop.BalanceError=null;});
            }
        },false));
        if(!String.IsNullOrEmpty(shop.BalanceFile)){
            panel.Controls.Add(LabelFor(shop.BalanceFile,10));
            panel.Controls.Add(ButtonFor("Отключить автопроверку",()=>Commit(()=>{shop.BalanceFile=null;shop.BalanceError=null;}),false));
        }
    }
}
