using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using System.Net;
using System.IO;

namespace GC_OPC_UA_Client
{
    class CleanDB
    {
       
        public void CleanCrap()
        {
            
            using (var connection = new SqliteConnection("Data Source=" + settings.DataBaseFileAndPath + ";Cache=Shared"))
            {
                connection.Open();
                        string sqlUpdate = "Delete From PLCTagChanged WHERE  Tag='2:ExhaustTemp1Raw' or Tag='2:ExhaustTempTrue' or Tag='2:ExhaustTemp1True' or Tag='2:ExhaustTemp2True' or Tag='2:ExhaustTemp3True' or Tag='2:ExhaustTemp4True' or Tag='2:ExhaustTemp5True' or Tag='2:HMIHour' or Tag='2:HMIHourBit' or Tag='2:HMIHours' or Tag='2:HMIMins' or Tag='2:HMIPM' or Tag='2:HotAirTempRaw' or Tag='2:Burner1MidCmd' or Tag='2:Burner2MidCmd'";
                        SqliteCommand executeCommand2 = new SqliteCommand(sqlUpdate, connection); // prepare query
                        try
                        {
                            executeCommand2.ExecuteNonQuery();
                        }
                        catch (SqliteException sqlE)
                        {
                            LogHandler.WriteLogFile("Error cleaning Database:" + sqlE.Message + " " + sqlE.StackTrace);
                        }
                        catch (Exception e)
                        {
                            LogHandler.WriteLogFile("Error cleaning Database:" + e.Message + " " + e.StackTrace);
                        }


                string sqlUpdate2 = "vacuum";
                SqliteCommand executeCommand3 = new SqliteCommand(sqlUpdate2, connection); // prepare query
                try
                {
                    executeCommand3.ExecuteNonQuery();
                }
                catch (SqliteException sqlE)
                {
                    LogHandler.WriteLogFile("Error vacuum Database:" + sqlE.Message + " " + sqlE.StackTrace);
                }
                catch (Exception e)
                {
                    LogHandler.WriteLogFile("Error vacuum Database:" + e.Message + " " + e.StackTrace);
                }

                connection.Close();     // close connection with db file
            }
        }
      
    }
}
