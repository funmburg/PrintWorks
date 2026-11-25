using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

public class Machine {
    public string Name, Area, Number;
    public DateTime NextService, LastService; public string Basis=Shop.Days, ServiceWork="Регламентное обслуживание"; public int Interval=30; public long Threshold=100,HourCount,CycleCount;
    public override string ToString() { return Name + " · " + Number; }
}
public class Ticket {
    public int Id; public long MaxOwner;
    public string Machine, Description, Kind, Priority, Status, Worker;
    public string Cause, Work, Parts;
    public decimal Hours; public bool Scheduled;
    public DateTime Created, Closed;
    public List<string> Log = new List<string>();
    public override string ToString() { return "№" + Id + "   " + Machine + "   ·   " + Status; }
}
public partial class Shop {
    public const string New = "Новая", Accepted = "Принята", Diagnosis = "Диагностика",
        Repair = "В ремонте", Review = "На приёмке", Closed = "Закрыта", Canceled = "Отменена";
    public List<Machine> Machines = new List<Machine>();
    public List<Ticket> Tickets = new List<Ticket>();
    public long Revision;
    static readonly XmlSerializer Format = new XmlSerializer(typeof(Shop));

    public static Shop Load(string path) {
        Shop newest = null;
        Exception damaged = null;
        foreach (string candidate in new[] {path, path + ".alternate"}) {
            if (!File.Exists(candidate)) continue;
            try {
                using (var input = File.OpenRead(candidate)) {
                    var saved = (Shop)Format.Deserialize(input);
                    if (newest == null || saved.Revision > newest.Revision) newest = saved;
                }
            } catch (InvalidOperationException e) { damaged = e; }
        }
        if (newest != null) return newest;
        if (damaged != null) throw new InvalidDataException("Neither saved copy of the shop can be read.", damaged);

        var shop = new Shop();
        string[] names = {"Офсетная машина", "Цифровая машина", "Резальная машина", "Фальцевальная машина", "Ламинатор", "Клеевая линия"};
        string[] areas = {"Офсетная печать", "Цифровая печать", "Резка", "Фальцовка", "Ламинация", "Переплёт"};
        for (int i = 0; i < names.Length; i++)
            shop.Machines.Add(new Machine { Name = names[i], Area = areas[i], Number = "P-" + (101 + i), NextService = DateTime.Today.AddDays(4 + i * 3) });
        shop.Add(shop.Machines[0], "Вибрация на подаче бумаги", "Срочно", "Ремонт", "Заявитель");
        return shop;
    }

    public void Save(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        long next = Revision + 1;
        string target = next % 2 == 1 ? path : path + ".alternate";
        Revision = next;
        try {
            using (var output = File.Create(target)) {
                Format.Serialize(output, this);
                output.Flush(true);
            }
        } catch {
            Revision--;
            throw;
        }
    }


    public Ticket Add(Machine machine, string description, string priority, string kind, string author) {
        if (String.IsNullOrWhiteSpace(description)) throw new ArgumentException("Введите описание работ или неисправности.");
        var ticket = new Ticket {
            Id = Tickets.Count == 0 ? 1 : Tickets.Max(t => t.Id) + 1,
            Machine = machine.Number, Description = description.Trim(), Priority = priority,
            Kind = kind, Status = New, Created = DateTime.Now
        };
        Record(ticket, author, "Создана заявка");
        Tickets.Add(ticket); AlertOnce("new/"+ticket.Id,"Исполнитель","Новая заявка №"+ticket.Id+" · "+machine.Name,machine,ticket,DateTime.Now); if(priority=="Авария") AlertOnce("alarm/"+ticket.Id,"Руководитель","Авария · "+machine.Area+" · "+machine.Name,machine,ticket,DateTime.Now);
        return ticket;
    }

    public static string[] Actions(Ticket t, string role) {
        if (role == "Ответственный" && t.Status == Review) return new[] { Closed, Repair };
        if (role == "Заявитель" && t.Status == New && !t.Scheduled) return new[] { Canceled };
        if (role != "Исполнитель") return new string[0];
        switch (t.Status) {
            case New: return new[] { Accepted };
            case Accepted: return new[] { Diagnosis, Repair, New };
            case Diagnosis: return new[] { Repair, New };
            case Repair: return new[] { Review, New };
            default: return new string[0];
        }
    }

    public static void Change(Ticket t, string to, string role, string note) {
        if (!Actions(t, role).Contains(to)) throw new InvalidOperationException("Этот переход недоступен.");
        if (to == Review && (String.IsNullOrWhiteSpace(t.Cause) || String.IsNullOrWhiteSpace(t.Work) || t.Hours <= 0))
            throw new InvalidOperationException("Заполните причину, выполненные работы и трудозатраты.");
        if (t.Status == Review && to == Repair && String.IsNullOrWhiteSpace(note))
            throw new InvalidOperationException("Укажите причину возврата.");
        string from = t.Status;
        t.Status = to;
        if (to == Accepted) t.Worker = role;
        if (to == New) t.Worker = null;
        if (to == Closed) t.Closed = DateTime.Now;
        Record(t, role, from + " → " + to + (String.IsNullOrWhiteSpace(note) ? "" : " · " + note.Trim()));
    }

    public static void Record(Ticket ticket, string author, string message) {
        ticket.Log.Add(DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "  " + author + "  ·  " + message);
    }
}
