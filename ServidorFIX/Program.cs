using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;
using QuickFix.Logger;
using QuickFix.Store;
using System;
using System.Collections.Generic;

class FixServer : MessageCracker, IApplication
{
    private readonly List<NewOrderSingle> _orders = new();
    private readonly Dictionary<string, decimal> _exposures = new();
    private const decimal ExposureLimit = 100_000_000m;

    public void FromApp(QuickFix.Message message, SessionID sessionID)
    {
        Console.WriteLine("Mensagem recebida do cliente.");
        Crack(message, sessionID);
    }

    public void OnMessage(NewOrderSingle order, SessionID sessionID)
    {
        _orders.Add(order);
        var symbol = order.Symbol.getValue();
        var qty = order.OrderQty.getValue();
        var price = order.Price.getValue();
        decimal orderValue = price * qty;

        _exposures.TryGetValue(symbol, out decimal currentExposure);
        decimal newExposure = order.Side.Equals(Side.BUY) ? currentExposure + orderValue : currentExposure - orderValue;

        Console.WriteLine($"> Nova Ordem: {symbol} {qty}x {price} ({(order.Side.Equals(Side.BUY) ? "Compra" : "Venda")})");

        var execReport = new ExecutionReport(
            new OrderID(order.ClOrdID.Value),
            new ExecID(Guid.NewGuid().ToString()),
            new ExecType(Math.Abs(newExposure) > ExposureLimit ? ExecType.REJECTED : ExecType.NEW),
            new OrdStatus(Math.Abs(newExposure) > ExposureLimit ? OrdStatus.REJECTED : OrdStatus.NEW),
            order.Symbol,
            order.Side,
            new LeavesQty(0),
            new CumQty(qty),  // Adicionado
            new AvgPx(price)
        );
        execReport.SetField(new OrderQty(qty));
        execReport.SetField(new Price(price));

        if (Math.Abs(newExposure) > ExposureLimit)
        {
            execReport.SetField(new Text("Limite de exposição excedido"));
        }
        else
        {
            _exposures[symbol] = newExposure;
        }

        Session.SendToTarget(execReport, sessionID);
    }

    public void FromAdmin(QuickFix.Message message, SessionID sessionID) { }
    public void OnCreate(SessionID sessionID) { }
    public void OnLogon(SessionID sessionID) => Console.WriteLine($"Cliente conectado. {sessionID}");
    public void OnLogout(SessionID sessionID) => Console.WriteLine($"Cliente desconectado. {sessionID}");
    public void ToAdmin(QuickFix.Message message, SessionID sessionID) { }
    public void ToApp(QuickFix.Message message, SessionID sessionID) { }
}

class Program
{
    static void Main()
    {
        var settings = new SessionSettings("fix.cfg");
        var app = new FixServer();
        var storeFactory = new FileStoreFactory(settings);
        var logFactory = new FileLogFactory(settings);
        var acceptor = new ThreadedSocketAcceptor(app, storeFactory, settings, logFactory);

        acceptor.Start();
        Console.WriteLine("Servidor iniciado. Pressione Enter para encerrar...");
        Console.ReadLine();
        acceptor.Stop();
    }
}
