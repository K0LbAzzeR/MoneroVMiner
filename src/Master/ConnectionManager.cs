﻿// Grin Gold Miner https://github.com/urza/GrinGoldMiner
// Copyright (c) 2018 Lukas Kubicek - urza
// Copyright (c) 2018 Jiri Vadura - photon

//#define CHINA

#define PRINTDEBUG
#undef PRINTDEBUG

using SharedSerialization;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Mozkomor.GrinGoldMiner
{
    public class ConnectionManager
    {
        public static volatile int userSolutions = 0;
        public static volatile int solutionCounter = 0; 
        private static volatile int solutionRound = 1000; 
        private static volatile int solverswitchmin = 200; 
        private static volatile int solverswitchmax = 600; 
        private static int solverswitch = 200; 
        private static volatile int prepConn = 10;
        private static volatile uint totalsolutionCounter = 0;
        private static volatile uint totalsolmfcnt = 0;
        private static volatile uint totalsolgfcnt = 0;
        private static volatile bool IsGfConnecting = false;
        private static volatile bool isMfConnecting = false;
        private static volatile bool stopConnecting = false;
        private static DateTime roundTime;

        private static StratumConnet curr_m;
        private static StratumConnet con_m1;
        private static StratumConnet con_m2;

        private static StratumConnet curr_mf;
        private static StratumConnet con_mf1;
        private static StratumConnet con_mf2;

        private static StratumConnet curr_gf;
        private static StratumConnet con_gf1;
        private static StratumConnet con_gf2;

        /// 
        /// GGM collects 1% as fee for the Grin Development Fund and 1% for further miner development.
        /// GGM is open source and solvers are released under fair mining licence,
        /// thanks for plyaing fair and keeping the fees here. It allows continuing developmlent of Grin and GGM.
        public static bool IsInFee() => (GetCurrentEpoch() != Episode.user);
        private static string gf_login = "grincouncil@protonmail.com/ggm3";
        private static string mf_login = "ggmfee0115@protonmail.com/ggm3";

        public static void Init(Config config, string algo)
        {
            //main
            con_m1 = new StratumConnet(config.PrimaryConnection.ConnectionAddress, config.PrimaryConnection.ConnectionPort, 1, config.PrimaryConnection.Login, config.PrimaryConnection.Password, config.PrimaryConnection.Ssl, algo);
            con_m2 = new StratumConnet(config.SecondaryConnection.ConnectionAddress, config.SecondaryConnection.ConnectionPort, 2, config.SecondaryConnection.Login, config.SecondaryConnection.Password, config.SecondaryConnection.Ssl, algo);

            //miner dev
            //con_mf1 = new StratumConnet("us-east-stratum.grinmint.com", 4416, 3, mf_login, "", true);
            //con_mf2 = new StratumConnet("eu-west-stratum.grinmint.com", 4416, 4, mf_login, "", true);
            //girn dev
            //con_gf1 = new StratumConnet("us-east-stratum.grinmint.com", 4416, 5, gf_login, "", true);
            //con_gf2 = new StratumConnet("eu-west-stratum.grinmint.com", 4416, 6, gf_login, "", true);


            solutionCounter = 0;
            solverswitch = new Random(DateTime.UtcNow.Millisecond).Next(solverswitchmin, solverswitchmax);
            Logger.Log(LogLevel.DEBUG, $"solverswitch {solverswitch}");

            roundTime = DateTime.Now;
            stopConnecting = false;
            ConnectMain();

            if (config.ReconnectToPrimary > 0)
                Task.Factory.StartNew(() => CheckPrimary(config.ReconnectToPrimary), TaskCreationOptions.LongRunning);
        }

        private static void checkFeeAvailability()
        {
            var feeConn = IsFeePortOpen();
            if (feeConn == null)
            {
                printFeeWarning();
                burnSols = 20;
                Console.ReadKey();
                Environment.Exit(-3);
            }
            else
            {
                con_mf1 = feeConn;
            }
        }

        private static void printFeeWarning()
        {
            Logger.Log(LogLevel.ERROR, "GGM collects 1% as fee for the Grin Development Fund and 1% for further miner development.");
            Logger.Log(LogLevel.ERROR, "Please allow these connections: ");
            Logger.Log(LogLevel.ERROR, "us-east-stratum.grinmint.com, port 4416");
            Logger.Log(LogLevel.ERROR, "eu-west-stratum.grinmint.com, port 4416");
        }

        static StratumConnet IsFeePortOpen()
        {
            Logger.Log(LogLevel.DEBUG, $"Checking FEE connectios.");
            List<StratumConnet> feeConnects = new List<StratumConnet>()
            {
                con_mf1,
                con_mf2,
            };

            foreach (var conn in feeConnects)
            {
                try
                {
                    //Console.ForegroundColor = ConsoleColor.Magenta;
                    //Logger.Log(LogLevel.DEBUG, $"Checking FEE connections. {conn.ip}");
                    //Console.ResetColor();
                    conn.Connect();
                    if (conn.IsConnected)
                    {
                        conn.SendLogin();
                        int i = 0;
                        while(conn.IsLoginConfirmed != true && i < 40)
                        {
                            Task.Delay(500).Wait();
                            i++;
                        }

                        if (conn.IsLoginConfirmed)
                        {
                            //Console.ForegroundColor = ConsoleColor.Magenta;
                            //Logger.Log(LogLevel.DEBUG, $"Checking FEE connections. OK {conn.ip}");
                            //Console.ResetColor();
                            tryCloseConn(conn); //close the fee connection//this chcek is at the beginning/end of round, whre fee conn is not needed
                            return conn;
                        }

                        tryCloseConn(conn); //close the fee connection//this chcek is at the beginning/end of round, whre fee conn is not needed
                        //Console.ForegroundColor = ConsoleColor.Magenta;
                        //Logger.Log(LogLevel.DEBUG, $"Checking FEE connections. FAILED TO LOGIN. {conn.ip}");
                        //Console.ResetColor();
                    }
                    //Logger.Log(LogLevel.DEBUG, $"Checking FEE connections. FAILED TO CONNECT. {conn.ip}");
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.DEBUG, "fee check " + ex.Message);
                }
            }
            return null;
        }

        public static void CheckPrimary(int minutes)
        {
            while (true)
            {
                Task.Delay(TimeSpan.FromMinutes(minutes)).Wait();
                TryPrimary();
            }
        }

        public static void TryPrimary()
        {
            if (con_m1?.IsConnected == false)
            {
                if (con_m2?.IsConnected == true)
                {
                    con_m1.Connect();
                    if (con_m1?.IsConnected == true)
                    {
                        con_m1.ReconnectAction = ReconnectMain;
                        con_m1.SendLogin();

                        curr_m = con_m1;
                        curr_m.RequestJob();

                        con_m2.StratumClose();
                    }
                }
            }
        }

        public static string SetConnection(Connection connection)
        {
            try
            {
                //change connection M1 from API
                Logger.Log(LogLevel.INFO, "Setting new StratumConnection from API as Primary connection.");
                Logger.Log(LogLevel.INFO, "This will change connection in running instance of GGM miner.");
                Logger.Log(LogLevel.INFO, "To save connection permanently to config, use /api/config [POST]");
                Logger.Log(LogLevel.INFO, "Closing current Primary connection...");
                con_m1.StratumClose();
                if (con_m2?.IsConnected == true)
                    con_m2.StratumClose();

                Task.Delay(4000).Wait();
                Logger.Log(LogLevel.INFO, $"setting Primary to {connection.Login} :: {connection.ConnectionAddress}:{connection.ConnectionPort}");
                con_m1 = new StratumConnet(connection.ConnectionAddress, connection.ConnectionPort, 1, connection.Login, connection.Password, connection.Ssl);
                stopConnecting = false; //bug fix for special situation when primary connection is down, and pushing new connection via API is in situation that stopConnecting =true and both m1 and m2 are IsConnecte==false.. then it was hanging without possibility to connect
                ConnectMain(chooseRandom: false); //primary first
                return "ok";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

#region connecting
        //main (user) connection is disconnected, pause workers (stop burning electricity), disconnect fees, wait for reconnection
        private static void PauseAllMining()
        {
            if (curr_mf?.IsConnected == true)
                curr_mf.StratumClose();

            if (curr_gf?.IsConnected == true)
                curr_gf.StratumClose();

            Task.Delay(1000).Wait();

            WorkerManager.PauseAllWorkers();

            solutionCounter = 0;
        }

        private static void ConnectMain(bool chooseRandom = false)
        {

            bool connected = false;

            DateTime started = DateTime.Now;

            int i = 1;
            var rnd = new Random(DateTime.Now.Millisecond);

            while (!connected && !stopConnecting)
            {
                if (DateTime.Now - started > TimeSpan.FromSeconds(60))
                {
                    //main connection not available for more than 60s
                    PauseAllMining();
                }

                i = Math.Min(60, i);
                i++;
                Task.Delay(i * 1000).Wait();

                var flip = rnd.NextDouble();

                if (chooseRandom)
                {
                    Logger.Log(LogLevel.DEBUG, $"reconnecting rnd {flip}");
                    if (flip < 0.5)
                    {
                        con_m1.Connect();
                        if (con_m1.IsConnected)
                        {
                            curr_m = con_m1;
                            connected = true;
                            Logger.Log(LogLevel.DEBUG, "conection1 succ");
                        }
                    }
                    else
                    {
                        con_m2.Connect();
                        if (con_m2.IsConnected)
                        {
                            curr_m = con_m2;
                            connected = true;
                            Logger.Log(LogLevel.DEBUG, "conection2 succ");
                        }
                    }
                }
                else
                {
                    con_m1.Connect();
                    if (con_m1.IsConnected)
                    {
                        curr_m = con_m1;
                        connected = true;
                        Logger.Log(LogLevel.DEBUG, "conection1 succ");
                    }
                    else
                    {
                        if (con_m2 != null)
                        {
                            con_m2.Connect();
                            if (con_m2.IsConnected)
                            {
                                curr_m = con_m2;
                                connected = true;
                                Logger.Log(LogLevel.DEBUG, "conection2 succ");
                            }
                            else
                            {
                                Logger.Log(LogLevel.DEBUG, "both con1 & con2 failed, trying again");
                                //Task.Delay(1000).Wait();
                            }
                        }
                    }
                }
            }

            if (curr_m != null)
            {
                curr_m.ReconnectAction = ReconnectMain;
                curr_m.SendLogin();
                curr_m.RequestJob();
            }
        }

        public static void ReconnectMain()
        {
            Task.Delay(2500).Wait();
            Logger.Log(LogLevel.DEBUG, "trying to reconnect main...");
            // curr_m.StratumClose(); //already in watchdog
            curr_m = null;
            stopConnecting = false;
            ConnectMain(chooseRandom: true);
        }

        private static void ConnectMf()
        {
            Logger.Log(LogLevel.DEBUG, "conecting to mf");
            bool connected = false;
            isMfConnecting = true;
            int i = 1;
            while (!connected && !stopConnecting)
            {
                i = Math.Min(60, i);
                i++;
                Task.Delay(i * 1000).Wait();

                con_mf1.Connect();
                if (con_mf1.IsConnected)
                {
                    curr_mf = con_mf1;
                    connected = true;
                    Logger.Log(LogLevel.DEBUG, "conection1 mf succ");
                }
                else
                {
                    if (con_mf2 != null)
                    {
                        con_mf2.Connect();
                        if (con_mf2.IsConnected)
                        {
                            curr_mf = con_mf2;
                            connected = true;
                            Logger.Log(LogLevel.DEBUG, "conection2 mf succ");
                        }
                        else
                        {
                            Logger.Log(LogLevel.DEBUG, "both con1 & con2 mf failed, trying again");
                            Task.Delay(1000).Wait();
                        }
                    }
                }
            }

            if (curr_mf != null)
            {
                curr_mf.ReconnectAction = ReconnectMf;
                curr_mf.SendLogin();
                curr_mf.RequestJob();
            }

            isMfConnecting = false;
        }

        public static void ReconnectMf()
        {
            Logger.Log(LogLevel.DEBUG, "trying to reconnect...");
            curr_mf = null;
            stopConnecting = false;
            ConnectMf();
        }

        private static void ConnectGf()
        {
            bool connected = false;
            IsGfConnecting = true;
            int i = 0;

            while (!connected && !stopConnecting)
            {
                i = Math.Min(60, i);
                i++;
                Task.Delay(i * 1000).Wait();

                con_gf1.Connect();
                if (con_gf1.IsConnected)
                {
                    curr_gf = con_gf1;
                    connected = true;
                    Logger.Log(LogLevel.DEBUG, "conection1 gf succ");
                }
                else
                {
                    if (con_gf2 != null)
                    {
                        con_gf2.Connect();
                        if (con_gf2.IsConnected)
                        {
                            curr_gf = con_gf2;
                            connected = true;
                            Logger.Log(LogLevel.DEBUG, "conection2 gf succ");
                        }
                        else
                        {
                            Logger.Log(LogLevel.DEBUG, "both con1 & con2 failed, trying again");
                            Task.Delay(1000).Wait();
                        }
                    }
                }
            }

            if (curr_gf != null)
            {
                curr_gf.ReconnectAction = ReconnectGf;
                curr_gf.SendLogin();
                curr_gf.RequestJob();
            }

            IsGfConnecting = false;
        }

        public static void ReconnectGf()
        {
            Logger.Log(LogLevel.DEBUG, "trying to reconnect...");
            curr_gf = null;
            stopConnecting = false;
            ConnectGf();
        }

        public static void CloseAll()
        {
            con_m1.StratumClose();
            con_m2.StratumClose();
            //con_mf1.StratumClose();
            //con_mf2.StratumClose();
            //con_gf1.StratumClose();
            //con_gf2.StratumClose();
        }
#endregion

#region state
        public static bool IsConnectionCurrent(int id)
        {
            if ((id == 1 || id == 2) && GetCurrentEpoch() == Episode.user)
                return true;

            if ((id == 3 || id == 4) && GetCurrentEpoch() == Episode.mf)
                return true;

            if ((id == 5 || id == 6) && GetCurrentEpoch() == Episode.gf)
                return true;

            return false;
        }

        public static StratumConnet GetCurrConn()
        {
            var ep = GetCurrentEpoch();
            switch (ep)
            {
                case Episode.user:
                    return curr_m;
                case
                    Episode.mf:
                    return curr_mf;
                case Episode.gf:
                    return curr_gf;
            }

            return curr_m;
        }

        public static StratumConnet GetConnectionById(int id)
        {
            switch (id)
            {
                case 1: return con_m1;
                case 2: return con_m2;
                default: return null;
            }
        }

        private static Episode GetCurrentEpoch()
        {
            if (solutionCounter < solverswitch)
            {
                return Episode.user;
            }
            else if (solutionCounter < solverswitch + 10)
            {
                return Episode.mf;
            }
            else if (solutionCounter < solverswitch + 20)
            {
                return Episode.gf;
            }
            else
            {
                return Episode.user;
            }
        }
#endregion

#region submit
        public static string lock_submit = "";

        private static bool hasMfJob()
        {
            try
            {
                return curr_mf?.IsConnected == true && !string.IsNullOrEmpty(curr_mf?.CurrentJob?.pre_pow);
            }
            catch { return false; }
        }
        private static bool hasGfJob()
        {
            try
            {
                return curr_gf?.IsConnected == true && !string.IsNullOrEmpty(curr_gf?.CurrentJob?.pre_pow);
            }
            catch { return false; }
        }
        private static bool hasUserJob()
        {
            try
            {
                return curr_m?.IsConnected == true && !string.IsNullOrEmpty(curr_m?.CurrentJob?.pre_pow);
            }
            catch { return false; }
        }

        public static void SubmitSol(SharedSerialization.Solution solution)
        {
            Logger.Log(LogLevel.DEBUG, $"({solutionCounter}) SubmitSol...");

            lock (lock_submit)
            {

                var ep = GetCurrentEpoch();
                switch (ep)
                {
                    case Episode.mf:
                        if (hasMfJob() && solution.job.origin == Episode.user)
                        {
                            Logger.Log(LogLevel.DEBUG, $"({solutionCounter}) SubmitSol going out bc origin==user");
                            return;
                        }
                        break;
                    case Episode.gf:
                        if (hasGfJob() && solution.job.origin == Episode.mf)
                        {
                            Logger.Log(LogLevel.DEBUG, $"({solutionCounter}) SubmitSol going out bc origin==mf");
                            return;
                        }
                        break;
                    case Episode.user:
                        if (hasUserJob() && solution.job.origin == Episode.gf)
                        {
                            Logger.Log(LogLevel.DEBUG, $"({solutionCounter}) SubmitSol going out bc origin==gf");
                            return;
                        }
                        break;
                }

                if (solution.job.origin == ep)
                {
                    switch (ep)
                    {
                        case Episode.user:
                            if (curr_m?.IsConnected == true)
                            {
                                Logger.Log(LogLevel.DEBUG, $"({solutionCounter}) Submitting solution Connection id {curr_m.id} SOL: job id {solution.job.ToString()} origine {solution.job.origin.ToString()}. ");
                                curr_m.SendSolution(solution);
                                totalsolutionCounter++;
                                userSolutions++;
                            }
                            break;
                        case Episode.mf:
                            if (curr_mf?.IsConnected == true)
                            {
                                Logger.Log(LogLevel.DEBUG, $"({solutionCounter}) Submitting solution Connection id {curr_mf.id} SOL: job id {solution.job.ToString()} origine {solution.job.origin.ToString()}. ");
                                curr_mf.SendSolution(solution);
                                totalsolmfcnt++;
                            }
                            break;
                        case Episode.gf:
                            if (curr_gf?.IsConnected == true)
                            {
                                Logger.Log(LogLevel.DEBUG, $"({solutionCounter}) Submitting solution Connection id {curr_gf.id} SOL: job id {solution.job.ToString()} origine {solution.job.origin.ToString()}. ");
                                curr_gf.SendSolution(solution);
                                totalsolgfcnt++;
                            }
                            break;
                    }
                }
                else
                {
                    Logger.Log(LogLevel.DEBUG, $"({solutionCounter}) Cant be here. (origin != ep)");
                }

                solutionCounter++;

            }


        }

        private static void tryCloseConn(StratumConnet conn)
        {
            try
            {
                Task.Run(() => Task.Delay(5000).ContinueWith(_ =>
                {
                    try { conn.StratumClose(); } catch { }
                }
                ));
            }
            catch { }
        }

        private static volatile int burnSols = 0;
        private static void resetRound()
        {
            Logger.Log(LogLevel.DEBUG, $"({solutionCounter}) SWITCHER: resetting round, setting solutionCounter to 0");

            Task.Run(() => checkFeeAvailability()); //this must happen outside lock, otherwise we will not receive login before exiting (even with task.delay.wiat)
            
            solutionCounter = userSolutions = 0;
            var time = DateTime.Now - roundTime;
            roundTime = DateTime.Now;
            //based on solution time, try to target prepConn to 10 seconds but minimum 10 sols
            prepConn = (int)Math.Round(Math.Max(10, (10 / (time.TotalSeconds / solutionRound))));

            try
            {
                //Task.Run(() =>
                //{
                Logger.Log(LogLevel.DEBUG, $"Round reset: solT:{totalsolutionCounter}, mfT:{totalsolmfcnt}, gfT:{totalsolgfcnt}");
                Logger.Log(LogLevel.DEBUG, $"Round time: {(time).TotalSeconds}  seconds");
                Logger.Log(LogLevel.DEBUG, $"PrepConn: {prepConn}");
                Logger.Log(LogLevel.INFO, $"Avg sol time: {(time).TotalSeconds / solutionRound} seconds");
                //});
            }
            catch { }
        }
#endregion

        

    }

}

