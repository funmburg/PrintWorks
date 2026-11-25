using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

public class MaxDialog {
    public long User, Chat; public string Prompt;
    public string Step, Machine, Priority, LastEvent, LastReply;
}
public partial class Shop {
    public List<MaxDialog> MaxDialogs=new List<MaxDialog>();
    public string MaxMarker;
    public string Answer(long uid,string mid,string input) {
        return Answer(uid,0,mid,input,null);
    }
    public MaxDialog DialogFor(long uid,long chat) { return MaxDialogs.FirstOrDefault(x=>x.User==uid && x.Chat==chat); }
    public bool AcceptGroup(long uid,long chat,string mid,string text,string replyTo) {
        var d=DialogFor(uid,chat);
        string cmd=(text??"").Trim().ToLowerInvariant();
        return cmd=="/new" || cmd=="/help" || cmd=="/my" || (d!=null && (d.LastEvent==mid ||
            (d.Step!=null && (cmd=="/cancel" || (!String.IsNullOrEmpty(replyTo) && replyTo==d.Prompt)))));
    }
    public string Answer(long uid,long chat,string mid,string input,string replyTo) {
        if(chat!=0 && !AcceptGroup(uid,chat,mid,input,replyTo))return null;
        var d=DialogFor(uid,chat);
        if(d==null){d=new MaxDialog{User=uid,Chat=chat};MaxDialogs.Add(d);}
        if(d.LastEvent==mid)return d.LastReply;
        string text=(input??"").Trim();
        string answer;
        if(chat!=0 && (text.Equals("/my",StringComparison.OrdinalIgnoreCase) || text.Equals("/help",StringComparison.OrdinalIgnoreCase)))
            answer="Для заявки отправьте /new в этот чат. Список своих заявок можно посмотреть командой /my в личном диалоге с ботом.";
        else {
            answer=HandleText(d,text);
            if(chat!=0){
                d.Prompt=null;
                if(d.Step!=null)answer+="\nВыберите «Ответить» на это сообщение бота.";
                else if(answer.Contains("Проверить статус: /my"))answer=answer.Replace("Проверить статус: /my","Статус — в личке с ботом по /my.");
            }
        }
        d.LastEvent=mid; d.LastReply=answer; return answer;
    }
    string HandleText(MaxDialog d,string text){
        string cmd=text.ToLowerInvariant();
        if(cmd=="/start" || cmd=="/help" || cmd=="/cancel"){
            d.Step=null;return "Цифровой цех. /new — заявка на ремонт, /my — мои заявки, /cancel — отменить ввод.";
        }
        if(cmd=="/my"){
            var mine=Tickets.Where(t=>t.MaxOwner==d.User).OrderByDescending(t=>t.Id).Take(12).Select(t=>"№"+t.Id+" · "+t.Machine+" · "+t.Status).ToArray();
            return mine.Length==0?"У вас пока нет заявок. Начните с /new.":String.Join("\n",mine);
        }
        if(cmd=="/new"){
            d.Step="machine";return "Введите инвентарный номер станка:\n"+String.Join("\n",Machines.Select(m=>m.Number+" · "+m.Name));
        }
        if(d.Step=="machine"){
            var m=Machines.FirstOrDefault(x=>String.Equals(x.Number,text,StringComparison.OrdinalIgnoreCase));
            if(m==null)return "Станок не найден. Введите номер из списка, например P-101.";
            d.Machine=m.Number;d.Step="priority";return "Выберите срочность цифрой: 1 — Авария, 2 — Срочно, 3 — Планово.";
        }
        if(d.Step=="priority"){
            int ix;if(!Int32.TryParse(text,out ix)||ix<1||ix>3)return "Введите 1, 2 или 3.";
            d.Priority=new[]{"Авария","Срочно","Планово"}[ix-1];d.Step="description";return "Опишите неисправность одним сообщением.";
        }
        if(d.Step=="description"){
            if(text.Length<3||text.Length>2000||text.StartsWith("/"))return "Введите описание длиной от 3 до 2000 символов или /cancel.";
            var m=Machines.Single(x=>x.Number==d.Machine);
            var t=Add(m,text,d.Priority,"Ремонт","MAX "+d.User);t.MaxOwner=d.User;d.Step=null;
            return "Принята заявка №"+t.Id+" · "+m.Name+" · "+t.Priority+". Проверить статус: /my";
        }
        return "Для новой заявки отправьте /new. Для проверки статуса — /my.";
    }
}
partial class Desk {
    volatile bool botClosing;
    static IDictionary<string,object> Obj(IDictionary<string,object> parent,string key){
        object value;return parent!=null && parent.TryGetValue(key,out value)?value as IDictionary<string,object>:null;
    }
    static string Val(IDictionary<string,object> parent,string key){
        object value;return parent!=null && parent.TryGetValue(key,out value)?Convert.ToString(value):null;
    }
    void EnableMax(){
        string token=Environment.GetEnvironmentVariable("PRINTWORKS_MAX_TOKEN");
        if(String.IsNullOrWhiteSpace(token))return;
        var users=new HashSet<long>();
        foreach(string str in (Environment.GetEnvironmentVariable("PRINTWORKS_MAX_USERS")??"").Split(',')){
            long user;if(Int64.TryParse(str.Trim(),out user)&&user>0)users.Add(user);
        }
        if(users.Count==0){MessageBox.Show("Для MAX укажите ID сотрудников в PRINTWORKS_MAX_USERS.");return;}
        FormClosing+=(s,e)=>botClosing=true;
        Shown+=(s,e)=>{var thread=new Thread(()=>PollMax(token,users)){IsBackground=true};thread.Start();};
    }
    void PersistBot(Action action){
        try{action();shop.Save(path);}
        catch{shop=Shop.Load(path);throw;}
        Render();
    }
    static IDictionary<string,object> MaxRequest(string token,string route,string body){
        var req=(HttpWebRequest)WebRequest.Create("https://platform-api2.max.ru/"+route);
        req.Headers["Authorization"]=token;req.AllowAutoRedirect=false;req.Timeout=45000;
        if(body!=null){
            req.Method="POST";req.ContentType="application/json; charset=utf-8";
            byte[] bytes=Encoding.UTF8.GetBytes(body);using(var s=req.GetRequestStream())s.Write(bytes,0,bytes.Length);
        }
        using(var response=req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))
            return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
    }
    void PollMax(string token,HashSet<long> users){
        string marker=null;
        var json=new JavaScriptSerializer();
        ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072;
        string pending=Path.Combine(Path.GetDirectoryName(path),"max-pending.json");
        while(!botClosing){
            try{
                if(marker==null)marker=(string)Invoke(new Func<string>(()=>shop.MaxMarker));
                IDictionary<string,object> batch;
                if(File.Exists(pending))batch=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(pending));
                else{
                    batch=MaxRequest(token,"updates?limit=1&timeout=25&types=message_created,bot_started"+(marker==null?"":"&marker="+Uri.EscapeDataString(marker)),null);
                    var list=batch["updates"] as System.Collections.ICollection;
                    if(list!=null && list.Count>0){
                        File.WriteAllText(pending+".tmp",json.Serialize(batch));File.Move(pending+".tmp",pending);
                    }
                }
                foreach(var raw in (System.Collections.IEnumerable)batch["updates"]){
                    var u=raw as IDictionary<string,object>;bool start=Val(u,"update_type")=="bot_started";
                    var msg=Obj(u,"message");var user=start?Obj(u,"user"):Obj(msg,"sender");
                    var body=Obj(msg,"body");var dest=Obj(msg,"recipient");
                    long id;
                    if(!Int64.TryParse(Val(user,"user_id"),out id)||!users.Contains(id)||String.Equals(Val(user,"is_bot"),"true",StringComparison.OrdinalIgnoreCase))continue;
                    long chat=0;
                    if(!start && Val(dest,"chat_type")!="dialog"){
                        if(Val(dest,"chat_type")!="chat" || !Int64.TryParse(Val(dest,"chat_id"),out chat) || chat==0)continue;
                    }
                    var linked=Obj(msg,"link");
                    string replyTo=Val(linked,"type")=="reply"?Val(Obj(linked,"message"),"mid"):null;
                    string mid=start?"start/"+id+"/"+Val(u,"timestamp"):Val(body,"mid");if(String.IsNullOrEmpty(mid))continue;
                    string input=start?"/start":Val(body,"text");
                    string reply=(string)Invoke(new Func<string>(()=>{
                        if(chat!=0 && !shop.AcceptGroup(id,chat,mid,input,replyTo))return null; var d=shop.DialogFor(id,chat);
                        if(d!=null && d.LastEvent==mid)return d.LastReply;
                        string result=null;PersistBot(()=>result=shop.Answer(id,chat,mid,input,replyTo));return result;
                    }));
                    if(reply==null)continue;
                    if(chat==0)MaxRequest(token,"messages?user_id="+id,json.Serialize(new{text=reply}));
                    else {
                        var sent=MaxRequest(token,"messages?chat_id="+chat,json.Serialize(new{text=reply,link=new{type="reply",mid=mid}}));
                        string prompt=Val(Obj(Obj(sent,"message"),"body"),"mid");
                        if(String.IsNullOrEmpty(prompt))throw new InvalidDataException("Нет ID ответа MAX");
                        Invoke(new Action(()=>{
                            var d=shop.DialogFor(id,chat);
                            if(d.Step!=null && !String.Equals((input??"").Trim(),"/my",StringComparison.OrdinalIgnoreCase) && !String.Equals((input??"").Trim(),"/help",StringComparison.OrdinalIgnoreCase))
                                PersistBot(()=>d.Prompt=prompt);
                        }));
                    }
                    Thread.Sleep(550);
                }
                string next=Val(batch,"marker");
                if(!String.IsNullOrEmpty(next)&&next!=marker){
                    Invoke(new Action(()=>PersistBot(()=>shop.MaxMarker=next)));marker=next;
                }
                if(File.Exists(pending))File.Delete(pending);
                BeginInvoke(new Action(()=>Text="Цифровой цех · MAX подключён"));
            }catch(Exception ex){
                if(botClosing)return;
                var web=ex as WebException;var response=web==null?null:web.Response as HttpWebResponse;
                string why=response==null?ex.GetType().Name:((int)response.StatusCode).ToString();
                if(response!=null)response.Dispose();
                try{BeginInvoke(new Action(()=>Text="Цифровой цех · ошибка MAX ("+why+"), повтор через 10 с"));}catch(InvalidOperationException){return;}
                Thread.Sleep(10000);
            }
        }
    }
}
