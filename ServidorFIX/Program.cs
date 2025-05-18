using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;
using QuickFix.Logger;
using QuickFix.Store;
using ServidorFIX;
using System;
using System.Collections.Generic;

class FixServer : MessageCracker, IApplication
{
    private readonly RedisExposureStore _exposureStore = new();
    private const decimal ExposureLimit = 100_000_000m;

    public void FromApp(QuickFix.Message message, SessionID sessionID)
    {
        Console.WriteLine("Mensagem recebida do cliente.");
        Crack(message, sessionID);
    }

    public void OnMessage(NewOrderSingle order, SessionID sessionID)
    {
        var symbol = order.Symbol.Value;
        var side = order.Side;
        var qty = order.OrderQty.Value;
        var price = order.Price.Value;
        var clOrdID = order.ClOrdID;

        decimal orderValue = qty * price;
        decimal currentExposure = _exposureStore.GetExposure(symbol);

        decimal newExposure = side.getValue() == Side.BUY
            ? currentExposure + orderValue
            : currentExposure - orderValue;

        bool excedeuLimite = Math.Abs(newExposure) > ExposureLimit;

        Console.WriteLine($"Ordem recebida: {symbol} {qty}x{price:0.00} ({(side.getValue() == Side.BUY ? "Compra" : "Venda")})");
        Console.WriteLine($"Exposição atual: {currentExposure:0.00} > {(excedeuLimite ? "EXCEDIDO" : newExposure.ToString("0.00"))}");

        var execType = excedeuLimite ? ExecType.REJECTED : ExecType.NEW;
        var ordStatus = excedeuLimite ? OrdStatus.REJECTED : OrdStatus.NEW;

        var execReport = new ExecutionReport(
            new OrderID(Guid.NewGuid().ToString()),
            new ExecID(Guid.NewGuid().ToString()),
            new ExecType(execType),
            new OrdStatus(ordStatus),
            new Symbol(symbol),
            side,
            new LeavesQty(0),
            new CumQty(qty),
            new AvgPx(price)
        );

        execReport.SetField(clOrdID);
        execReport.SetField(order.OrderQty);
        execReport.SetField(order.Price);
        execReport.Set(new LastQty(qty));
        execReport.Set(new LastPx(price));

        if (excedeuLimite)
            execReport.Set(new Text("Limite de exposição excedido"));
        else
            _exposureStore.SetExposure(symbol, newExposure);

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
    private const string HttpServerPrefix = "http://127.0.0.1:5080/";
    static void Main()
    {
        var settings = new SessionSettings("fix.cfg");
        var app = new FixServer();
        var storeFactory = new FileStoreFactory(settings);
        var logFactory = new FileLogFactory(settings);
        var acceptor = new ThreadedSocketAcceptor(app, storeFactory, settings, logFactory);

        //Somente para analisar o fluxo da sessão(não usar em prod, conforme orientação da documentação)
        HttpServer srv = new HttpServer(HttpServerPrefix, settings);

        acceptor.Start();
        srv.Start();
        Console.WriteLine("Ver status do servidor: " + HttpServerPrefix);
        Console.WriteLine("Servidor iniciado. Pressione Enter para encerrar...");
        Console.ReadLine();
        srv.Stop();
        acceptor.Stop();
    }
}
