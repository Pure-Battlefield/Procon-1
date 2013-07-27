/*  Copyright 2010 Imisnew2

    http://www.TeamPlayerGaming.com/members/Imisnew2.html

    This file is part of Cameron "Imisnew2" Gunnin's Teamspeak 3 Sync Plugin
    for PRoCon.

    Imisnew2's Teamspeak 3 Sync Plugin for PRoCon is free software:
    you can redistribute it and/or modify it under the terms of the GNU
    General Public License as published by the Free Software Foundation,
    either version 3 of the License, or (at your option) any later version.

    Imisnew2's Teamspeak 3 Sync Plugin for PRoCon is distributed in
    the hope that it will be useful, but WITHOUT ANY WARRANTY; without even
    the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
    See the GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Imisnew2's Teamspeak 3 Sync Plugin for PRoCon.
    If not, see <http://www.gnu.org/licenses/>.

*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using PRoCon.Core;
using PRoCon.Core.Players;
using PRoCon.Core.Plugin;

namespace PRoConEvents
{
    /// <summary>
    /// Manages a Teamspeak 3 Server, synchronizing it with a game server.
    /// Moves players between specified channels corresponding to their team/squad in
    /// the game server. Also allows 'Messages' to be sent to players who are not on
    /// your Teamspeak server, yet playing in your game server.
    /// </summary
    class TeamspeakSync : CPRoConMarshalByRefObject, IPRoConPluginInterface
    {
        #region Teamspeak API

        #region Main Classes

        /// <summary>Holds information about squds in game that are also in Teamspeak.</summary>
        public class TsGameSquadInfo
        {
            /// <summary>Number of players in this squad in-game. </summary>
            public int InGameCount = 0;
            /// <summary>Number of players in this squad in TS. </summary>
            public int TsCount = 0;
        }

        /// <summary>Holds all the information needed to connect to a TS3 server.</summary>
        public class TeamspeakConnection
        {
            // Ok.
            private static TeamspeakResponse TSR_OK = new TeamspeakResponse("error id=0 msg=ok");
            // Open.
            private static TeamspeakResponse TSR_OPEN_ERR_1 = new TeamspeakResponse("error id=-1 msg=The\\sconnection\\swas\\sreopened\\swhen\\sthe\\sprevious\\sconnection\\swas\\sstill\\sopen.");
            private static TeamspeakResponse TSR_OPEN_ERR_2 = new TeamspeakResponse("error id=-2 msg=Invalid\\sIP\\sAddress.");
            private static TeamspeakResponse TSR_OPEN_ERR_3 = new TeamspeakResponse("error id=-3 msg=Invalid\\sPort.");
            private static TeamspeakResponse TSR_OPEN_ERR_4 = new TeamspeakResponse("error id=-4 msg=An\\serror\\soccurred\\swhen\\strying\\sto\\sestablish\\sa\\sconnection.");
            // Send.
            private static TeamspeakResponse TSR_SEND_ERR_1 = new TeamspeakResponse("error id=-5 msg=The\\sconnection\\swas\\sclosed\\swhen\\sa\\squery\\swas\\stried\\sto\\sbe\\ssent.");
            private static TeamspeakResponse TSR_SEND_ERR_2 = new TeamspeakResponse("error id=-6 msg=The\\squery\\sto\\sbe\\ssent\\swas\\snull.");
            private static TeamspeakResponse TSR_SEND_ERR_3 = new TeamspeakResponse("error id=-7 msg=An\\serror\\soccurred\\swhen\\sthe\\squery\\swas\\ssent.");
            private static TeamspeakResponse TSR_SEND_ERR_4 = new TeamspeakResponse("error id=-8 msg=An\\serror\\soccurred\\swhen\\sthe\\sresponse\\swas\\sreceived.");
            // The Socket
            public Socket Socket;


            /// <summary>Creates a socket to be used for connecting to a Teamspeak server.</summary>
            public TeamspeakConnection()
            {
                Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                Socket.SendTimeout = 5000;
                Socket.ReceiveTimeout = 5000;
            }


            /// <summary>Establish a connection to a Teamspeak 3 Server.</summary>
            /// <returns>TeamspeakResponse - How the method performed.</returns>
            public TeamspeakResponse open(String ip, UInt16 port)
            {
                // Error checking.
                if (Socket.Connected) return TSR_OPEN_ERR_1;
                if (String.IsNullOrEmpty(ip)) return TSR_OPEN_ERR_2;
                if (port == 0) return TSR_OPEN_ERR_3;

                // Establish Connection.
                String rBuffer = String.Empty;
                Byte[] sBuffer = new Byte[2048];
                try
                {
                    Socket.Connect(ip, port);

                    Thread.Sleep(1000);
                    Int32 size = Socket.Receive(sBuffer, sBuffer.Length, SocketFlags.None);
                    rBuffer += Encoding.Default.GetString(sBuffer, 0, size);

                    if (!rBuffer.Contains("TS3"))
                        throw new Exception();
                }
                catch (Exception) { close(); return TSR_OPEN_ERR_4; }
                OnDataReceived(rBuffer);

                // Done
                if (rBuffer.Contains("error id="))
                    return new TeamspeakResponse(rBuffer);
                return TSR_OK;
            }
            /// <summary>Closes the connection to a Teamspeak 3 Server.</summary>
            /// <returns>TeamspeakResponse - How the method performed.</returns>
            public TeamspeakResponse close()
            {
                Socket.Close();
                Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                Socket.SendTimeout = 5000;
                Socket.ReceiveTimeout = 5000;
                return TSR_OK;
            }
            /// <summary>Sends a query and blocks until a response is received.</summary>
            /// <returns>The response from the server.</returns>
            public TeamspeakResponse send(TeamspeakQuery query)
            {
                // Error Check.
                if (!Socket.Connected) return TSR_SEND_ERR_1;
                if (query == null) return TSR_SEND_ERR_2;

                String rBuffer = null;
                Byte[] sBuffer = null;

                // Send the query.
                try
                {
                    rBuffer = query.rawQuery();
                    sBuffer = Encoding.Default.GetBytes(rBuffer);
                    Socket.Send(sBuffer, rBuffer.Length, SocketFlags.None);
                }
                catch (Exception) { close(); return TSR_SEND_ERR_3; }
                OnDataSent(rBuffer);

                // Receive the response.
                rBuffer = String.Empty;
                sBuffer = new Byte[65536];
                DateTime start = DateTime.Now;
                while (!rBuffer.Contains("error id=") || !rBuffer.EndsWith("\n\r"))
                    try
                    {
                        Int32 size = Socket.Receive(sBuffer, sBuffer.Length, SocketFlags.None);
                        rBuffer += Encoding.Default.GetString(sBuffer, 0, size);
                        if ((DateTime.Now - start).TotalMilliseconds > 5500) break;
                    }
                    catch (Exception) { close(); return TSR_SEND_ERR_4; }
                OnDataReceived(rBuffer);

                // Send back the response.
                return new TeamspeakResponse(rBuffer);
            }

            // For Teamspeak 3 Sync purposes only.
            public delegate void DataHandler(String data);
            public event DataHandler DataSent;
            public event DataHandler DataReceived;
            private void OnDataSent(String data)
            {
                if (DataSent != null)
                    DataSent(data.Trim());
            }
            private void OnDataReceived(String data)
            {
                if (DataReceived != null)
                    DataReceived(data.Trim());
            }
        }

        /// <summary>Parses all the information contained in a response from a Teamspeak 3 server.</summary>
        public class TeamspeakResponse
        {
            // -- Back-end storage for the response and sections.
            private String tsRaw = null;
            private TeamspeakResponseGroup tsError = null;
            private List<TeamspeakResponseSection> tsSections = null;

            // -- Accessors for the response.
            public String RawResponse { get { return tsRaw; } }
            public String Id { get { return tsError["id"]; } }
            public String Message { get { return tsError["msg"]; } }
            public String ExtraMessage { get { return tsError["extra_msg"]; } }

            // -- Accessor and Qualifier for the sections.
            public Boolean HasSections { get { return tsSections.Count != 0; } }
            public ReadOnlyCollection<TeamspeakResponseSection> Sections { get { return tsSections.AsReadOnly(); } }



            /// <summary>Parses a Teamspeak 3 response into sections.</summary>
            public TeamspeakResponse(String rawResponse) { parse(rawResponse); }

            /// <summary>Takes a raw response and parses it into sections.</summary>
            private void parse(string raw)
            {
                // Set Class Variables.
                tsRaw = raw.Replace("\n", @"\n").Replace("\r", @"\r");
                tsError = new TeamspeakResponseGroup("empty");
                tsSections = new List<TeamspeakResponseSection>();

                // Split the response up into sections and remove invalid lines.
                foreach (String section in raw.Replace("\n\r", "\n").Split('\n'))
                    if (section.Contains("error id="))
                        tsError = new TeamspeakResponseGroup(section.Trim());
                    else if (!String.IsNullOrEmpty(section.Trim()))
                        tsSections.Add(new TeamspeakResponseSection(section.Trim()));
            }
        }

        /// <summary>Parses all the information contained in a section (denoted by '\n\r') of a response from a Teamspeak 3 server.</summary>
        public class TeamspeakResponseSection
        {
            // -- Back-end storage for the response and groups.
            private String tsRaw = null;
            private List<TeamspeakResponseGroup> tsGroups = new List<TeamspeakResponseGroup>();

            // -- Accessors for the response.
            public String RawSection { get { return tsRaw; } }

            // -- Accessor and Qualifier for the groups.
            public Boolean HasGroups { get { return tsGroups.Count != 0; } }
            public ReadOnlyCollection<TeamspeakResponseGroup> Groups { get { return tsGroups.AsReadOnly(); } }



            /// <summary>Parses a section of a Teamspeak 3 response into groups.</summary>
            public TeamspeakResponseSection(String rawSection) { parse(rawSection); }

            /// <summary>Takes a raw section response and parses it into groups.</summary>
            private void parse(String raw)
            {
                // Set Class Variables.
                tsRaw = raw;
                tsGroups = new List<TeamspeakResponseGroup>();

                // Split the section up into groups.
                foreach (String group in raw.Split('|'))
                    tsGroups.Add(new TeamspeakResponseGroup(group.Trim()));
            }
        }

        /// <summary>Parses all the information contained in a group (denoted by '|') of a response from a Teamspeak 3 server.</summary>
        public class TeamspeakResponseGroup
        {
            // -- Back-end storage for the response and pairs.
            private String tsRaw = null;
            private Dictionary<String, String> tsPairs = new Dictionary<String, String>();

            // -- Accessors for the response and pairs.
            public String RawGroup { get { return tsRaw; } }
            public String this[String key] { get { return (tsPairs.ContainsKey(key)) ? tsPairs[key] : null; } }



            /// <summary>Parses a group of a Teamspeak 3 response into key/value pairs.</summary>
            public TeamspeakResponseGroup(String rawGroup) { parse(rawGroup); }

            /// <summary>Takes a raw group response and parses it into key/value pairs.</summary>
            private void parse(String raw)
            {
                // Set Class Variables.
                tsRaw = raw;
                tsPairs = new Dictionary<String, String>();

                // Split the group up into key/value pairs and discard invalid pairs.
                foreach (string element in raw.Split(' '))
                    if (element.Contains("="))
                    {
                        // For some reason, a key is being received twice for the same response.
                        // This causes the program to crash.  To work around this, if a key is received
                        // twice, it simply stores the last value received.
                        String[] pair = element.Split('=');
                        if (tsPairs.ContainsKey(pair[0]))
                        {
                            tsPairs[pair[0]] = TeamspeakHelper.ts_UnescapeString(pair[1]);
                        }
                        else
                        {
                            tsPairs.Add(pair[0], TeamspeakHelper.ts_UnescapeString(pair[1]));
                        }
                    }
            }
        }

        /// <summary>Contains all the information necessary for a Teamspeak 3 query to be built.</summary>
        public class TeamspeakQuery
        {
            // -- Back-end storage for the command, parameters, and options.
            private String tsCommand = null;
            private Dictionary<String, String> tsParameters = null;
            private List<String> tsOptions = null;

            // -- Accessor for the command.
            public String Command { get { return tsCommand; } }



            /// <summary>Creates a query using the specified command.</summary>
            public TeamspeakQuery(String command)
            {
                tsCommand = command;
                tsParameters = new Dictionary<String, String>();
                tsOptions = new List<String>();
            }

            /// <summary>Adds a key/value pair to this query.</summary>
            public void addParameter(String key, String value)
            {
                String tKey = key.Trim();
                String tValue = value.Trim();
                if (!String.IsNullOrEmpty(tKey) && !String.IsNullOrEmpty(tValue))
                    if (!tsParameters.ContainsKey(tKey))
                        tsParameters.Add(TeamspeakHelper.ts_EscapeString(tKey), TeamspeakHelper.ts_EscapeString(tValue));
            }
            /// <summary>Adds an option to this query.</summary>
            public void addOption(String option)
            {
                String tOption = option.Trim();
                if (!String.IsNullOrEmpty(tOption))
                    tsOptions.Add(TeamspeakHelper.ts_EscapeString(tOption));
            }
            /// <summary>Removes a key/value pair from this query.</summary>
            public void removeParameter(String key)
            {
                String tKey = key.Trim();
                if (!String.IsNullOrEmpty(tKey))
                    tsParameters.Remove(tKey);
            }
            /// <summary>Removes an option from this query.</summary>
            public void removeOption(String option)
            {
                String tOption = option.Trim();
                if (!String.IsNullOrEmpty(tOption))
                    tsOptions.Remove(tOption);
            }

            /// <summary>Gets the raw query string for this query.</summary>
            public String rawQuery()
            {
                StringBuilder rawQuery = new StringBuilder();

                // Append the command.
                rawQuery.Append(tsCommand);
                // Append the parameters.
                foreach (KeyValuePair<String, String> p in tsParameters)
                    rawQuery.AppendFormat(" {0}={1}", p.Key, p.Value);
                // Append the options.
                foreach (String o in tsOptions)
                    rawQuery.AppendFormat(" -{0}", o);
                // Append a new line.
                rawQuery.Append("\n");

                return rawQuery.ToString();
            }


            #region Pre-Built Queries

            /// <summary>Builds a login query for you.</summary>
            /// <param name="username">The username to use.</param>
            /// <param name="password">The password to use.</param>
            public static TeamspeakQuery buildLoginQuery(String username, String password)
            {
                TeamspeakQuery tsLogin = new TeamspeakQuery("login");
                tsLogin.addParameter("client_login_name", username);
                tsLogin.addParameter("client_login_password", password);
                return tsLogin;
            }

            /// <summary>Builds a clientupdate query that changes the server query's nickname.</summary>
            /// <param name="newNickname">The nickname to change to.</param>
            public static TeamspeakQuery buildChangeNicknameQuery(String newNickname)
            {
                TeamspeakQuery tsClientUpdate = new TeamspeakQuery("clientupdate");
                tsClientUpdate.addParameter("client_nickname", newNickname);
                return tsClientUpdate;
            }

            /// <summary>Builds a serverlist query for you.</summary>
            public static TeamspeakQuery buildServerListQuery()
            {
                return new TeamspeakQuery("serverlist");
            }

            /// <summary>Builds a use query for you.</summary>
            /// <param name="virtualId">The virtual server Id to use.</param>
            public static TeamspeakQuery buildUseVIdQuery(Int32 virtualId)
            {
                TeamspeakQuery tsUse = new TeamspeakQuery("use");
                tsUse.addParameter("sid", virtualId.ToString());
                return tsUse;
            }

            /// <summary>Builds a use query for you.</summary>
            /// <param name="port">The port of the virtual server to use.</param>
            public static TeamspeakQuery buildUsePortQuery(Int32 port)
            {
                TeamspeakQuery tsUse = new TeamspeakQuery("use");
                tsUse.addParameter("port", port.ToString());
                return tsUse;
            }

            /// <summary>Builds a channellist query for you.</summary>
            public static TeamspeakQuery buildChannelListQuery()
            {
                return new TeamspeakQuery("channellist");
            }

            /// <summary>Builds a channelfind query for you.</summary>
            /// <param name="channelName">The channel name to use.</param>
            public static TeamspeakQuery buildChannelFindQuery(String channelName)
            {
                TeamspeakQuery tsChannelFind = new TeamspeakQuery("channelfind");
                tsChannelFind.addParameter("pattern", channelName);
                return tsChannelFind;
            }

            /// <summary>Builds a channelinfo query for you.</summary>
            /// <param name="channelId">The channel id to use.</param>
            public static TeamspeakQuery buildChannelInfoQuery(Int32 channelId)
            {
                TeamspeakQuery tsChannelInfo = new TeamspeakQuery("channelinfo");
                tsChannelInfo.addParameter("cid", channelId.ToString());
                return tsChannelInfo;
            }

            /// <summary>Builds a clientlist query for you.</summary>
            public static TeamspeakQuery buildClientListQuery()
            {
                return new TeamspeakQuery("clientlist");
            }

            /// <summary>Builds a clientfind query for you.</summary>
            /// <param name="clientName">The client name to use.</param>
            public static TeamspeakQuery buildClientFindQuery(String clientName)
            {
                TeamspeakQuery tsClientFind = new TeamspeakQuery("clientfind");
                tsClientFind.addParameter("pattern", clientName);
                return tsClientFind;
            }

            /// <summary>Builds a clientinfo query for you.</summary>
            /// <param name="clientId">The client id to use.</param>
            public static TeamspeakQuery buildClientInfoQuery(Int32 clientId)
            {
                TeamspeakQuery tsClientInfo = new TeamspeakQuery("clientinfo");
                tsClientInfo.addParameter("clid", clientId.ToString());
                return tsClientInfo;
            }

            /// <summary>Builds a clientmove query for you.</summary>
            /// <param name="clientId">The client id to use.</param>
            /// <param name="channelId">The channel id to use.</param>
            public static TeamspeakQuery buildClientMoveQuery(Int32 clientId, Int32 channelId)
            {
                TeamspeakQuery tsClientMove = new TeamspeakQuery("clientmove");
                tsClientMove.addParameter("clid", clientId.ToString());
                tsClientMove.addParameter("cid", channelId.ToString());
                return tsClientMove;
            }

            #endregion
        }

        #endregion
        #region Objects

        /// <summary>Contains all possible information about a server.</summary>
        public class TeamspeakServer
        {
            #region Basic Server Data

            // Data received from a "serverlist" command
            //-- Identifying Data
            public String tsName = null;  //virtualserver_name
            public Int32? tsId = null;  //virtualserver_id
            public Int32? tsPort = null;  //virtualserver_port
            public Int32? tsMachineId = null;  //virtualserver_machine_id
            //-- Status Data
            public String tsStatus = null;  //virtualserver_status
            public Int32? tsUpTime = null;  //virtualserver_uptime
            public Int32? tsClientsOnline = null;  //virtualserver_clientsonline
            public Int32? tsQueryClientsOnline = null;  //virtualserver_queryclientsonline
            //-- Misc Data
            public Int32? tsQueryMaxClients = null;  //virtualserver_maxclients
            public Boolean? tsAutoStart = null;  //virtualserver_autostart

            #endregion

            /// <summary>Default constructor.</summary>
            public TeamspeakServer() { }
            /// <summary>Attempts to set all the data from the passed in response.</summary>
            /// <param name="serverInfo">The server's information to set from.</param>
            public TeamspeakServer(TeamspeakResponseGroup serverInfo)
            {
                setBasicData(serverInfo);
            }

            /// <summary>Sets all the basic data for a Teamspeak server.</summary>
            /// <param name="serverInfo">The server's information to set from.</param>
            public void setBasicData(TeamspeakResponseGroup serverInfo)
            {
                String value;
                Int32 iValue;
                Boolean bValue;

                tsName = serverInfo["virtualserver_name"];
                if ((value = serverInfo["virtualserver_id"]) != null) if (Int32.TryParse(value, out iValue)) tsId = iValue; else tsId = null; else tsId = null;
                if ((value = serverInfo["virtualserver_port"]) != null) if (Int32.TryParse(value, out iValue)) tsPort = iValue; else tsPort = null; else tsPort = null;
                if ((value = serverInfo["virtualserver_machine_id"]) != null) if (Int32.TryParse(value, out iValue)) tsMachineId = iValue; else tsMachineId = null; else tsMachineId = null;

                tsStatus = serverInfo["virtualserver_status"];
                if ((value = serverInfo["virtualserver_uptime"]) != null) if (Int32.TryParse(value, out iValue)) tsUpTime = iValue; else tsUpTime = null; else tsUpTime = null;
                if ((value = serverInfo["virtualserver_clientsonline"]) != null) if (Int32.TryParse(value, out iValue)) tsClientsOnline = iValue; else tsClientsOnline = null; else tsClientsOnline = null;
                if ((value = serverInfo["virtualserver_queryclientsonline"]) != null) if (Int32.TryParse(value, out iValue)) tsQueryClientsOnline = iValue; else tsQueryClientsOnline = null; else tsQueryClientsOnline = null;

                if ((value = serverInfo["virtualserver_maxclients"]) != null) if (Int32.TryParse(value, out iValue)) tsQueryMaxClients = iValue; else tsQueryMaxClients = null; else tsQueryMaxClients = null;
                if ((value = serverInfo["virtualserver_autostart"]) != null) if (Boolean.TryParse(value, out bValue)) tsAutoStart = bValue; else tsAutoStart = null; else tsAutoStart = null;
            }
        }

        /// <summary>Contains all possible information about a channel.</summary>
        public class TeamspeakChannel
        {
            #region Basic Channel Data

            // Data received from a "channelfind" command
            public String tsName = null;  //channel_name
            public Int32? tsId = null;  //cid

            #endregion
            #region Medium Channel Data

            // Data received from a "channellist" command
            public Int32? medPId = null;  //pid
            public Int32? medOrder = null;  //channel_order
            public Int32? medTotalClients = null;  //total_clients
            public Int32? medPowerNeededToSub = null;  //channel_needed_subscribe_power

            #endregion
            #region Advanced Channel Data

            // Additional Data received from a "channelinfo" command
            public String advTopic = null;  //channel_topic
            public String advDescription = null;  //channel_description
            public String advPassword = null;  //channel_password
            public String advFilepath = null;  //channel_filepath
            public String advPhoneticName = null;  //channel_name_phonetic
            public Int32? advCodec = null;  //channel_codec
            public Int32? advCodecQuality = null;  //channel_codec_quality
            public Int32? advCodecLatencyFactor = null;  //channel_codec_latency_factor
            public Int32? advMaxClients = null;  //channel_maxclients
            public Int32? advMaxFamilyClients = null;  //channel_maxfamilyclients
            public Int32? advNeededTalkPower = null;  //channel_needed_talk_power
            public Int32? advIconId = null;  //channel_icon_id
            public Boolean? advFlagPermanent = null;  //channel_flag_permanent
            public Boolean? advFlagSemiPermanent = null;  //channel_flag_semi_permanent
            public Boolean? advFlagDefault = null;  //channel_flag_default
            public Boolean? advFlagPassword = null;  //channel_flag_password
            public Boolean? advFlagMaxClientsUnlimited = null;  //channel_flag_maxclients_unlimited
            public Boolean? advFlagMaxFamilyClientsUnlimited = null;  //channel_flag_maxfamilyclients_unlimited
            public Boolean? advFlagMaxFamilyClientsInherited = null;  //channel_flag_maxfamilyclients_inherited
            public Boolean? advForcedSilence = null;  //channel_forced_silence

            #endregion

            /// <summary>Default constructor.</summary>
            public TeamspeakChannel() { }
            /// <summary>Attempts to set all the data from the passed in response.</summary>
            /// <param name="channelInfo">The channel's information to set from.</param>
            public TeamspeakChannel(TeamspeakResponseGroup channelInfo)
            {
                setBasicData(channelInfo);
                setMediumData(channelInfo);
                setAdvancedData(channelInfo);
            }

            /// <summary>Sets all the basic data for a Teamspeak channel.</summary>
            /// <param name="channelInfo">The channel's information to set from.</param>
            public void setBasicData(TeamspeakResponseGroup channelInfo)
            {
                String value;
                Int32 iValue;

                tsName = channelInfo["channel_name"];
                if ((value = channelInfo["cid"]) != null) if (Int32.TryParse(value, out iValue)) tsId = iValue; else tsId = null; else tsId = null;
            }

            /// <summary>Sets all the medium data for a Teamspeak channel.</summary>
            /// <param name="channelInfo">The channel's information to set from.</param>
            public void setMediumData(TeamspeakResponseGroup channelInfo)
            {
                String value;
                Int32 iValue;

                if ((value = channelInfo["pid"]) != null) if (Int32.TryParse(value, out iValue)) medPId = iValue; else medPId = null; else medPId = null;
                if ((value = channelInfo["channel_order"]) != null) if (Int32.TryParse(value, out iValue)) medOrder = iValue; else medOrder = null; else medOrder = null;
                if ((value = channelInfo["total_clients"]) != null) if (Int32.TryParse(value, out iValue)) medTotalClients = iValue; else medTotalClients = null; else medTotalClients = null;
                if ((value = channelInfo["channel_needed_subscribe_power"]) != null) if (Int32.TryParse(value, out iValue)) medPowerNeededToSub = iValue; else medPowerNeededToSub = null; else medPowerNeededToSub = null;
            }

            /// <summary>Sets all the advanced data for a Teamspeak channel.</summary>
            /// <param name="channelInfo">The channel's information to set from.</param>
            public void setAdvancedData(TeamspeakResponseGroup channelInfo)
            {
                String value;
                Int32 iValue;
                Boolean bValue;

                advTopic = channelInfo["channel_topic"];
                advDescription = channelInfo["channel_description"];
                advPassword = channelInfo["channel_password"];
                advFilepath = channelInfo["channel_filepath"];
                advPhoneticName = channelInfo["channel_name_phonetic"];
                if ((value = channelInfo["channel_codec"]) != null) if (Int32.TryParse(value, out iValue)) advCodec = iValue; else advCodec = null; else advCodec = null;
                if ((value = channelInfo["channel_codec_quality"]) != null) if (Int32.TryParse(value, out iValue)) advCodecQuality = iValue; else advCodecQuality = null; else advCodecQuality = null;
                if ((value = channelInfo["channel_codec_latency_factor"]) != null) if (Int32.TryParse(value, out iValue)) advCodecLatencyFactor = iValue; else advCodecLatencyFactor = null; else advCodecLatencyFactor = null;
                if ((value = channelInfo["channel_maxclients"]) != null) if (Int32.TryParse(value, out iValue)) advMaxClients = iValue; else advMaxClients = null; else advMaxClients = null;
                if ((value = channelInfo["channel_maxfamilyclients"]) != null) if (Int32.TryParse(value, out iValue)) advMaxFamilyClients = iValue; else advMaxFamilyClients = null; else advMaxFamilyClients = null;
                if ((value = channelInfo["channel_needed_talk_power"]) != null) if (Int32.TryParse(value, out iValue)) advNeededTalkPower = iValue; else advNeededTalkPower = null; else advNeededTalkPower = null;
                if ((value = channelInfo["channel_icon_id"]) != null) if (Int32.TryParse(value, out iValue)) advIconId = iValue; else advIconId = null; else advIconId = null;
                if ((value = channelInfo["channel_flag_permanent"]) != null) if (Boolean.TryParse(value, out bValue)) advFlagPermanent = bValue; else advFlagPermanent = null; else advFlagPermanent = null;
                if ((value = channelInfo["channel_flag_semi_permanent"]) != null) if (Boolean.TryParse(value, out bValue)) advFlagSemiPermanent = bValue; else advFlagSemiPermanent = null; else advFlagSemiPermanent = null;
                if ((value = channelInfo["channel_flag_default"]) != null) if (Boolean.TryParse(value, out bValue)) advFlagDefault = bValue; else advFlagDefault = null; else advFlagDefault = null;
                if ((value = channelInfo["channel_flag_password"]) != null) if (Boolean.TryParse(value, out bValue)) advFlagPassword = bValue; else advFlagPassword = null; else advFlagPassword = null;
                if ((value = channelInfo["channel_flag_maxclients_unlimited"]) != null) if (Boolean.TryParse(value, out bValue)) advFlagMaxClientsUnlimited = bValue; else advFlagMaxClientsUnlimited = null; else advFlagMaxClientsUnlimited = null;
                if ((value = channelInfo["channel_flag_maxfamilyclients_unlimited"]) != null) if (Boolean.TryParse(value, out bValue)) advFlagMaxFamilyClientsUnlimited = bValue; else advFlagMaxFamilyClientsUnlimited = null; else advFlagMaxFamilyClientsUnlimited = null;
                if ((value = channelInfo["channel_flag_maxfamilyclients_inherited"]) != null) if (Boolean.TryParse(value, out bValue)) advFlagMaxFamilyClientsInherited = bValue; else advFlagMaxFamilyClientsInherited = null; else advFlagMaxFamilyClientsInherited = null;
                if ((value = channelInfo["channel_forced_silence"]) != null) if (Boolean.TryParse(value, out bValue)) advForcedSilence = bValue; else advForcedSilence = null; else advForcedSilence = null;
            }
        }

        /// <summary>Contains all possible information about a client.</summary>
        public class TeamspeakClient
        {
            #region Basic Client Data

            // Data received from a "clientfind" command
            public String tsName = null;  //client_nickname
            public Int32? tsId = null;  //clid

            #endregion
            #region Medium Client Data

            // Data received from a "clientlist" command
            public Int32? medDatabaseId = null;  //client_database_id
            public Int32? medChannelId = null;  //cid
            public Int32? medType = null;  //client_type

            #endregion
            #region Advanced Client Data

            // Additional Data received from a "clientinfo" command
            //-- Identifying Data
            public String advLoginName = null;  //client_login_name
            public String advUniqueId = null;  //client_unique_identifier
            public String advIpAddress = null;  //connection_client_ip
            //-- Meta Data
            public String advVersion = null;  //client_version
            public String advPlatform = null;  //client_platform
            public String advDescription = null;  //client_description
            public String advCountry = null;  //client_country
            public String advMetaData = null;  //client_meta_data
            //-- Permissions Data
            public Int32? advChannelGroupId = null;  //client_channel_group_id
            public Int32? advServerGroupId = null;  //client_servergroups
            public Boolean? advIsChannelCommander = null;  //client_is_channel_commander
            //-- Server Statistics Data
            public String advDefaultChannel = null;  //client_default_channel
            public Int32? advConnectionTime = null;  //connection_connected_time
            public Int32? advIdleTime = null;  //client_idle_time
            public Int32? advCreationTime = null;  //client_created
            public Int32? advLastConnected = null;  //client_lastconnected
            public Int32? advTotalConnections = null;  //client_totalconnections
            //-- Microphone Data
            public Boolean? advInputMuted = null;  //client_input_muted
            public Boolean? advOutputMuted = null;  //client_output_muted
            public Boolean? advOutputMutedOnly = null;  //client_outputonly_muted
            public Boolean? advInputHardware = null;  //client_input_hardware
            public Boolean? advOutputHardware = null;  //client_output_hardware
            public Boolean? advIsRecording = null;  //client_is_recording
            //-- Misc Data
            public String advFlagAvatar = null;  //client_flag_avatar
            public String advAwayMessage = null;  //client_away_message
            public String advTalkMessage = null;  //client_talk_request_msg
            public String advPhoneticNick = null;  //client_nickname_phonetic
            public String advDefaultToken = null;  //client_default_token
            public String advBase64Hash = null;  //client_base64HashClientUID
            public Int32? advTalkPower = null;  //client_talk_power
            public Int32? advQueryViewPower = null;  //client_needed_serverquery_view_power
            public Int32? advUnreadMessages = null;  //client_unread_messages
            public Int32? advIconId = null;  //client_icon_id
            public Boolean? advIsAway = null;  //client_away
            public Boolean? advTalkRequest = null;  //client_talk_request
            public Boolean? advIsTalker = null;  //client_is_talker
            public Boolean? advIsPriority = null;  //client_is_priority_speaker
            //-- Bandwidth Data
            public Int32? advBytesUpMonth = null;  //client_month_bytes_uploaded
            public Int32? advBytesDownMonth = null;  //client_month_bytes_downloaded
            public Int32? advBytesUpTotal = null;  //client_total_bytes_uploaded
            public Int32? advBytesDownTotal = null;  //client_total_bytes_downloaded
            public Int32? advFileBandwidthSent = null;  //connection_filetransfer_bandwidth_sent
            public Int32? advFileBandwidthRec = null;  //connection_filetransfer_bandwidth_received
            public Int32? advPacketsTotalSent = null;  //connection_packets_sent_total
            public Int32? advPacketsTotalRec = null;  //connection_packets_received_total
            public Int32? advBytesTotalSent = null;  //connection_bytes_sent_total
            public Int32? advBytesTotalRec = null;  //connection_bytes_received_total
            public Int32? advBndwdthSecondSent = null;  //connection_bandwidth_sent_last_second_total
            public Int32? advBndwdthSecondRec = null;  //connection_bandwidth_received_last_second_total
            public Int32? advBndwdthMinuteSent = null;  //connection_bandwidth_sent_last_minute_total
            public Int32? advBndwdthMinuteRec = null;  //connection_bandwidth_received_last_minute_total

            #endregion

            /// <summary>Default constructor.</summary>
            public TeamspeakClient() { }
            /// <summary>Attempts to set all the data from the passed in response.</summary>
            /// <param name="clientInfo">The client's information to set from.</param>
            public TeamspeakClient(TeamspeakResponseGroup clientInfo)
            {
                setBasicData(clientInfo);
                setMediumData(clientInfo);
                setAdvancedData(clientInfo);
            }

            /// <summary>Sets all the basic data for a Teamspeak client.</summary>
            /// <param name="clientInfo">The client's information to set from.</param>
            public void setBasicData(TeamspeakResponseGroup clientInfo)
            {
                String value;
                Int32 iValue;

                tsName = clientInfo["client_nickname"];
                if ((value = clientInfo["clid"]) != null) if (Int32.TryParse(value, out iValue)) tsId = iValue; else tsId = null; else tsId = null;
            }

            /// <summary>Sets all the medium data for a Teamspeak client.</summary>
            /// <param name="clientInfo">The client's information to set from.</param>
            public void setMediumData(TeamspeakResponseGroup clientInfo)
            {
                String value;
                Int32 iValue;

                if ((value = clientInfo["client_database_id"]) != null) if (Int32.TryParse(value, out iValue)) medDatabaseId = iValue; else medDatabaseId = null; else medDatabaseId = null;
                if ((value = clientInfo["cid"]) != null) if (Int32.TryParse(value, out iValue)) medChannelId = iValue; else medChannelId = null; else medChannelId = null;
                if ((value = clientInfo["client_type"]) != null) if (Int32.TryParse(value, out iValue)) medType = iValue; else medType = null; else medType = null;
            }

            /// <summary>Sets all the advanced data for a Teamspeak client.</summary>
            /// <param name="clientInfo">The client's information to set from.</param>
            public void setAdvancedData(TeamspeakResponseGroup clientInfo)
            {
                String value;
                Int32 iValue;
                Boolean bValue;

                advLoginName = clientInfo["client_login_name"];
                advUniqueId = clientInfo["client_unique_identifier"];
                advIpAddress = clientInfo["connection_client_ip"];

                advVersion = clientInfo["client_version"];
                advPlatform = clientInfo["client_platform"];
                advDescription = clientInfo["client_description"];
                advCountry = clientInfo["client_country"];
                advMetaData = clientInfo["client_meta_data"];

                if ((value = clientInfo["client_channel_group_id"]) != null) if (Int32.TryParse(value, out iValue)) advChannelGroupId = iValue; else advChannelGroupId = null; else advChannelGroupId = null;
                if ((value = clientInfo["client_servergroups"]) != null) if (Int32.TryParse(value, out iValue)) advServerGroupId = iValue; else advServerGroupId = null; else advServerGroupId = null;
                if ((value = clientInfo["client_is_channel_commander"]) != null) if (Boolean.TryParse(value, out bValue)) advIsChannelCommander = bValue; else advIsChannelCommander = null; else advIsChannelCommander = null;

                advDefaultChannel = clientInfo["client_default_channel"];
                if ((value = clientInfo["connection_connected_time"]) != null) if (Int32.TryParse(value, out iValue)) advConnectionTime = iValue; else advConnectionTime = null; else advConnectionTime = null;
                if ((value = clientInfo["client_idle_time"]) != null) if (Int32.TryParse(value, out iValue)) advIdleTime = iValue; else advIdleTime = null; else advIdleTime = null;
                if ((value = clientInfo["client_created"]) != null) if (Int32.TryParse(value, out iValue)) advCreationTime = iValue; else advCreationTime = null; else advCreationTime = null;
                if ((value = clientInfo["client_lastconnected"]) != null) if (Int32.TryParse(value, out iValue)) advLastConnected = iValue; else advLastConnected = null; else advLastConnected = null;
                if ((value = clientInfo["client_totalconnections"]) != null) if (Int32.TryParse(value, out iValue)) advTotalConnections = iValue; else advTotalConnections = null; else advTotalConnections = null;

                if ((value = clientInfo["client_input_muted"]) != null) if (Boolean.TryParse(value, out bValue)) advInputMuted = bValue; else advInputMuted = null; else advInputMuted = null;
                if ((value = clientInfo["client_output_muted"]) != null) if (Boolean.TryParse(value, out bValue)) advOutputMuted = bValue; else advOutputMuted = null; else advOutputMuted = null;
                if ((value = clientInfo["client_outputonly_muted"]) != null) if (Boolean.TryParse(value, out bValue)) advOutputMutedOnly = bValue; else advOutputMutedOnly = null; else advOutputMutedOnly = null;
                if ((value = clientInfo["client_input_hardware"]) != null) if (Boolean.TryParse(value, out bValue)) advInputHardware = bValue; else advInputHardware = null; else advInputHardware = null;
                if ((value = clientInfo["client_output_hardware"]) != null) if (Boolean.TryParse(value, out bValue)) advOutputHardware = bValue; else advOutputHardware = null; else advOutputHardware = null;
                if ((value = clientInfo["client_is_recording"]) != null) if (Boolean.TryParse(value, out bValue)) advIsRecording = bValue; else advIsRecording = null; else advIsRecording = null;

                advFlagAvatar = clientInfo["client_flag_avatar"];
                advAwayMessage = clientInfo["client_away_message"];
                advTalkMessage = clientInfo["client_talke_request_msg"];
                advPhoneticNick = clientInfo["client_nickname_phonetic"];
                advDefaultToken = clientInfo["client_default_token"];
                advBase64Hash = clientInfo["client_base64HashClientUID"];
                if ((value = clientInfo["client_talk_power"]) != null) if (Int32.TryParse(value, out iValue)) advTalkPower = iValue; else advTalkPower = null; else advTalkPower = null;
                if ((value = clientInfo["client_needed_serverquery_view_power"]) != null) if (Int32.TryParse(value, out iValue)) advQueryViewPower = iValue; else advQueryViewPower = null; else advQueryViewPower = null;
                if ((value = clientInfo["client_unread_messages"]) != null) if (Int32.TryParse(value, out iValue)) advUnreadMessages = iValue; else advUnreadMessages = null; else advUnreadMessages = null;
                if ((value = clientInfo["client_icon_id"]) != null) if (Int32.TryParse(value, out iValue)) advIconId = iValue; else advIconId = null; else advIconId = null;
                if ((value = clientInfo["client_away"]) != null) if (Boolean.TryParse(value, out bValue)) advIsAway = bValue; else advIsAway = null; else advIsAway = null;
                if ((value = clientInfo["client_talk_request"]) != null) if (Boolean.TryParse(value, out bValue)) advTalkRequest = bValue; else advTalkRequest = null; else advTalkRequest = null;
                if ((value = clientInfo["client_is_talker"]) != null) if (Boolean.TryParse(value, out bValue)) advIsTalker = bValue; else advIsTalker = null; else advIsTalker = null;
                if ((value = clientInfo["client_is_priority_speaker"]) != null) if (Boolean.TryParse(value, out bValue)) advIsPriority = bValue; else advIsPriority = null; else advIsPriority = null;

                if ((value = clientInfo["client_month_bytes_uploaded"]) != null) if (Int32.TryParse(value, out iValue)) advBytesUpMonth = iValue; else advBytesUpMonth = null; else advBytesUpMonth = null;
                if ((value = clientInfo["client_month_bytes_downloaded"]) != null) if (Int32.TryParse(value, out iValue)) advBytesDownMonth = iValue; else advBytesDownMonth = null; else advBytesDownMonth = null;
                if ((value = clientInfo["client_total_bytes_uploaded"]) != null) if (Int32.TryParse(value, out iValue)) advBytesUpTotal = iValue; else advBytesUpTotal = null; else advBytesUpTotal = null;
                if ((value = clientInfo["client_total_bytes_downloaded"]) != null) if (Int32.TryParse(value, out iValue)) advBytesDownTotal = iValue; else advBytesDownTotal = null; else advBytesDownTotal = null;
                if ((value = clientInfo["connection_filetransfer_bandwidth_sent"]) != null) if (Int32.TryParse(value, out iValue)) advFileBandwidthSent = iValue; else advFileBandwidthSent = null; else advFileBandwidthSent = null;
                if ((value = clientInfo["connection_filetransfer_bandwidth_received"]) != null) if (Int32.TryParse(value, out iValue)) advFileBandwidthRec = iValue; else advFileBandwidthRec = null; else advFileBandwidthRec = null;
                if ((value = clientInfo["connection_packets_sent_total"]) != null) if (Int32.TryParse(value, out iValue)) advPacketsTotalSent = iValue; else advUnreadMessages = null; else advUnreadMessages = null;
                if ((value = clientInfo["connection_packets_received_total"]) != null) if (Int32.TryParse(value, out iValue)) advPacketsTotalSent = iValue; else advPacketsTotalSent = null; else advPacketsTotalSent = null;
                if ((value = clientInfo["connection_bytes_sent_total"]) != null) if (Int32.TryParse(value, out iValue)) advBytesTotalSent = iValue; else advBytesTotalSent = null; else advBytesTotalSent = null;
                if ((value = clientInfo["connection_bytes_received_total"]) != null) if (Int32.TryParse(value, out iValue)) advBytesTotalRec = iValue; else advBytesTotalRec = null; else advBytesTotalRec = null;
                if ((value = clientInfo["connection_bandwidth_sent_last_second_total"]) != null) if (Int32.TryParse(value, out iValue)) advBndwdthSecondSent = iValue; else advBndwdthSecondSent = null; else advBndwdthSecondSent = null;
                if ((value = clientInfo["connection_bandwidth_received_last_second_total"]) != null) if (Int32.TryParse(value, out iValue)) advBndwdthSecondRec = iValue; else advBndwdthSecondRec = null; else advBndwdthSecondRec = null;
                if ((value = clientInfo["connection_bandwidth_sent_last_minute_total"]) != null) if (Int32.TryParse(value, out iValue)) advBndwdthMinuteSent = iValue; else advBndwdthMinuteSent = null; else advBndwdthMinuteSent = null;
                if ((value = clientInfo["connection_bandwidth_received_last_minute_total"]) != null) if (Int32.TryParse(value, out iValue)) advBndwdthMinuteRec = iValue; else advBndwdthMinuteRec = null; else advBndwdthMinuteRec = null;
            }
        }

        #endregion
        #region Helpers

        /// <summary>Contains helper methods to quickly perform commonly used actions.</summary>
        public static class TeamspeakHelper
        {
            /// <summary>Escapes all special characters in a string to conform to TS3 standards.</summary>
            /// <param name="text">The text to escape.</param>
            /// <returns>The escaped text.</returns>
            public static String ts_EscapeString(String text)
            {
                // Replace all special characters with escaped characters.
                String escaped = text.Replace("\\", @"\\");
                escaped = escaped.Replace("/", @"\/");
                escaped = escaped.Replace(" ", @"\s");
                escaped = escaped.Replace("|", @"\p");
                escaped = escaped.Replace("\a", @"\a");
                escaped = escaped.Replace("\b", @"\b");
                escaped = escaped.Replace("\f", @"\f");
                escaped = escaped.Replace("\n", @"\n");
                escaped = escaped.Replace("\r", @"\r");
                escaped = escaped.Replace("\t", @"\t");
                escaped = escaped.Replace("\v", @"\v");
                return escaped;
            }

            /// <summary>Unescapes all special characters in a string from TS3 standards.</summary>
            /// <param name="text">The text to unescape.</param>
            /// <returns>The unescaped text.</returns>
            public static String ts_UnescapeString(String text)
            {
                // Replace all special characters with escaped characters.
                String unescaped = text.Replace(@"\\", "\\");
                unescaped = unescaped.Replace(@"\/", "/");
                unescaped = unescaped.Replace(@"\s", " ");
                unescaped = unescaped.Replace(@"\p", "|");
                unescaped = unescaped.Replace(@"\a", "\a");
                unescaped = unescaped.Replace(@"\b", "\b");
                unescaped = unescaped.Replace(@"\f", "\f");
                unescaped = unescaped.Replace(@"\n", "\n");
                unescaped = unescaped.Replace(@"\r", "\r");
                unescaped = unescaped.Replace(@"\t", "\t");
                unescaped = unescaped.Replace(@"\v", "\v");
                return unescaped;
            }
        }

        #endregion

        #endregion



        // -- Section 1 - Teamspeak 3 -----------------------------------------
        String ts3ServerIp = "Teamspeak Ip";
        UInt16 ts3ServerPort = 9987;
        UInt16 ts3QueryPort = 10011;
        String ts3QueryUsername = "Username";
        String ts3QueryPassword = "Password";
        String ts3QueryNickname = "TeamspeakSync";
        String ts3StgChannelName = "Staging Channel Name";
        Boolean ts3EnableDropoff = false;
        private String ts3DropoffChannelName = "Dropoff Channel Name";
        String[] ts3PckChannelNames = new String[] { };
        // -- Section 2 - Channels --------------------------------------------
        String chnPassword = "L0cke9";
        String[] chnTeamNames = new String[] { "TeamspeakSync Team 1", "TeamspeakSync Team 2", "TeamspeakSync Team 3", "TeamspeakSync Team 4" };
        String[] chnSquadNames = new String[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliet", "Kilo", "Lima", "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "Xray", "Yankee", "Zulu", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Zero" };
        Boolean chnRemoveOnEmpty = false;
        // -- Section 3 - Synchronization -------------------------------------
        Boolean synDelayQueries = false;
        Int32 synDelayQueriesAmount = 700;
        Int32 synUpdateInterval = 10000;
        Boolean synTeamBasedSwapping = true;
        Int32 synTeamBasedThreshold = 1;
        Boolean synIntermissionSwapping = true;
        Boolean synSquadBasedSwapping = false;
        Int32 synSquadBasedThreshold = 8;
        Int32 synSquadSizeMinimum = 2;
        Double synMatchingThreshold = 100;
        Boolean synRemoveClients = true;
        String[] synRemoveClientsWhitelist = new String[] { };
        // -- Section 4 - Error Handling --------------------------------------
        Boolean errReconnectOnError = true;
        Int32 errReconnectOnErrorAttempts = 20;
        Int32 errReconnectOnErrorInterval = 30000;
        // -- Section 5 - User Messages ---------------------------------------
        Boolean msgEnabled = false;
        Boolean msgOnJoin = false;
        Int32 msgOnJoinDelay = 300000;
        Int32 msgInterval = 1800000;
        String msgMessage = "This server is using Teamspeak 3 Sync.";
        Int32 msgDuration = 6;
        // -- Section 6 - Debug Information -----------------------------------
        Boolean dbgEvents = false;
        Boolean dbgClients = false;
        Boolean dbgChannels = false;
        Boolean dbgSwapping = false;
        Boolean dbgNetwork = false;
        Boolean dbgBouncer = false;


        // -- Command Enumerations --------------------------------------------
        public enum Commands
        {
            PluginEnabled,
            PluginDisabled,

            UpdateTsClientInfo,
            UpdateGmClientInfo,
            UpdatePbClientInfo,
            UpdateMsClientInfo,

            CheckAllClientsForSwapping,
            CheckAllClientsForRemoval,
            CheckAllClientsForMessaging,
            CheckClientForSwapping,
            CheckClientForRemoval,
            CheckClientForMessaging,
            CheckClientForMessagingAfterJoining,

            PlayerJoined,
            PlayerLeft,
            PlayerSpawned,
            PlayerSwappedTeamsOrSquads,
            ResetAllUsersSyncFlags,
            ResetUserSyncFlags,
            DisplayTSSquadList,
            SetNoSync,
            SetSyncToTeam,
            SetSyncToStaging
        }
        // -- Error Enumerations ----------------------------------------------
        public enum Queries
        {
            OpenConnectionEstablish,
            OpenConnectionLogin,
            OpenConnectionUse,
            OpenConnectionStaging,
            OpenConnectionNickname,

            TsInfoClientList,
            TsInfoChannelList,
            TsInfoClientInfo,

            CheckSwapStaging,
            CheckSwapTeam,
            CheckSwapSquad,
            CheckRemove,

            FindTeamChannelList,
            FindSquadChannelList,
            CreateTeamChannelQuery,
            CreateTeamChannelInfo,
            CreateSquadChannelQuery,
            CreateSquadChannelInfo,

            RemoveChannelsList,
            RemoveChannelsTeamQuery,
            RemoveChannelsSquadQuery
        }
        // -- Threading -------------------------------------------------------
        Thread mThreadMain = null;
        Thread mThreadMessage = null;
        Thread mThreadSynchronize = null;
        // -- Actions ---------------------------------------------------------
        Mutex mActionMutex = new Mutex();
        Semaphore mActionSemaphore = new Semaphore(0, Int32.MaxValue);
        Queue<ActionEvent> mActions = new Queue<ActionEvent>();
        // -- Connection Handling ---------------------------------------------
        TeamspeakConnection mTsConnection = new TeamspeakConnection();
        TeamspeakResponse mTsResponse = new TeamspeakResponse("error id=0 msg=ok");
        Boolean mTsReconnecting = false;
        DateTime mTsPrevSendTime = DateTime.Now;
        AutoResetEvent mTsReconnEvent = new AutoResetEvent(false);
        // -- Client Information ----------------------------------------------
        List<MasterClient> mClientAllInfo = new List<MasterClient>();
        List<TeamspeakClient> mClientTsInfo = new List<TeamspeakClient>();
        List<GameClient> mClientGmInfo = new List<GameClient>();
        Dictionary<String, CPunkbusterInfo> mClientPbInfo = new Dictionary<String, CPunkbusterInfo>();
        List<Int32> mClientWhitelist = new List<Int32>();
        // -- Channel Information ---------------------------------------------
        TeamspeakChannel mStagingChannel = new TeamspeakChannel();
        TeamspeakChannel mDropoffChannel = new TeamspeakChannel();
        List<TeamspeakChannel> mPickupChannels = new List<TeamspeakChannel>();
        Dictionary<Int32, TeamspeakChannel> mTeamChannels = new Dictionary<Int32, TeamspeakChannel>();
        Dictionary<Int32, Dictionary<Int32, TeamspeakChannel>> mSquadChannels = new Dictionary<Int32, Dictionary<Int32, TeamspeakChannel>>();
        // -- Miscellaneous Variables -----------------------------------------
        Boolean mEnabled = false;
        Boolean mBetweenRounds = false;
        Queue<Timer> mMessageTimers = new Queue<Timer>();
        ActionEvent mCurrentAction = null;
        ActionEvent mPreviousAction = null;
        // -- In-Game TS Commands ---------------------------------------------
        Boolean mEnableTSSquadList = false;
        Boolean mEnableTSStaging = false;
        Boolean mEnableTSTeam = false;
        Boolean mEnableTSNoSync = false;
        // -- Event Bouncer Variables -----------------------------------------
        Boolean mEnableBouncer = false;
        String mBouncerKickMessage = "Sorry, you must be on our TeamSpeak server to play here.";


        /// <summary>Holds the player/punkbuster info combo from various games.</summary>
        public class GameClient
        {
            // [0] The player's general info (Teams/Squads).
            // [1] The player's punkbuster info (IPs).
            private CPlayerInfo generalInfo = null;
            private CPunkbusterInfo punkbusterInfo = null;

            // Easy access to general info.
            public String Name { get { return generalInfo.SoldierName; } }
            public String Tags { get { return generalInfo.ClanTag; } }
            public Int32 TeamId { get { return generalInfo.TeamID; } }
            public Int32 SquadId { get { return generalInfo.SquadID; } }
            public Int32 Score { get { return generalInfo.Score; } }
            public Int32 Kills { get { return generalInfo.Kills; } }
            public Int32 Deaths { get { return generalInfo.Deaths; } }
            public Double KDR { get { return generalInfo.Kdr; } }
            public Int32 Ping { get { return generalInfo.Ping; } }
            public String GUID { get { return generalInfo.GUID; } }
            // Easy access to punkbuster info.
            public String IP { get { if (punkbusterInfo != null) return punkbusterInfo.Ip; else return null; } }
            public String SlotId { get { if (punkbusterInfo != null) return punkbusterInfo.SlotID; else return null; } }
            public String Country { get { if (punkbusterInfo != null) return punkbusterInfo.PlayerCountry; else return null; } }
            public String CountryCode { get { if (punkbusterInfo != null) return punkbusterInfo.PlayerCountryCode; else return null; } }
            // Some extra helpers.
            /// <summary>Whether this client has punkbuster info initialized.</summary>
            public Boolean HasPbInfo { get { return punkbusterInfo != null; } }
            /// <summary>Sets the general information.</summary>
            public CPlayerInfo GeneralInfo { get { return generalInfo; } set { if (value != null) generalInfo = value; } }
            /// <summary>Sets the punkbuster information.</summary>
            public CPunkbusterInfo PunkbusterInfo { get { return punkbusterInfo; } set { if (value != null) punkbusterInfo = value; } }

            /// <summary>Creates the GameClient without a punkbuster info.</summary>
            /// <param name="genInfo">The player's general information.</param>
            public GameClient(CPlayerInfo genInfo) { generalInfo = genInfo; }
            /// <summary>Creates the GameClient with everything.</summary>
            /// <param name="genInfo">The player's general information.</param>
            /// <param name="pbInfo">The player's punkbuster information.</param>
            public GameClient(CPlayerInfo genInfo, CPunkbusterInfo pbInfo) { generalInfo = genInfo; punkbusterInfo = pbInfo; }
        }
        /// <summary>Holds the combination of TS Client and GM Client information.</summary>
        public class MasterClient
        {
            // [0] The client's teamspeak information.
            // [1] The client's game      information.
            private TeamspeakClient tsClient = null;
            private GameClient gmClient = null;
            private Boolean noSync = false;
            private Boolean syncToStaging = false;
            private Boolean syncToTeam = false;

            /// <summary>Whether the master client has teamspeak information.</summary>
            public Boolean HasTsClient { get { return tsClient != null; } }
            /// <summary>Whether the master client has game information.</summary>
            public Boolean HasGmClient { get { return gmClient != null; } }
            /// <summary>Sets the teamspeak information.</summary>
            public TeamspeakClient TsClient { get { return tsClient; } set { tsClient = value; } }
            /// <summary>Sets the game information</summary>
            public GameClient GmClient { get { return gmClient; } set { gmClient = value; } }
            // <summary>Specifies whether this client is has opted to exempt himself/herself from swapping.</summary>
            public Boolean         IsNoSync    { get { return noSync; } set { noSync = value; } }
            /// <summary>Specifies whether the client should be force swapped to the staging channel. </summary>
            public Boolean         IsSyncToStaging     { get { return syncToStaging; } set { syncToStaging = value; } }
            /// <summary>Specifies whether the client should be force swapped to the team channel.  </summary>
            public Boolean IsSyncToTeam { get { return syncToTeam; } set { syncToTeam = value; } }

            /// <summary>Creates the MasterClient with teamspeak information.</summary>
            /// <param name="ts">The teamspeak information.</param>
            public MasterClient(TeamspeakClient ts) { tsClient = ts; }
            /// <summary>Creates the MasterClient with game information.</summary>
            /// <param name="gm">The game information.</param>
            public MasterClient(GameClient gm) { gmClient = gm; }

            /// <summary>Calculates the percent match of a substring in another string. The shortest string is used as the substring.</summary>
            /// <param name="s1">The first string.</param>
            /// <param name="s2">The second string.</param>
            /// <returns>The percent match.</returns>
            public static Double calcPercentMatch(String s1, String s2)
            {
                // Variables
                double max;
                double min;
                int levDist;

                // Always use the longest string first (for Levinshtein Distance).
                if (s1.Length >= s2.Length)
                {
                    max = s1.Length;
                    min = s2.Length;
                    levDist = calcLevenshteinDistance(s1, s2);
                }
                else
                {
                    max = s2.Length;
                    min = s1.Length;
                    levDist = calcLevenshteinDistance(s2, s1);
                }

                double percent = (max - levDist) / max;
                double maxPossMatch = min / max;

                // Calc percent to which the string matched.
                // Calc largest possible match (i.e difference in length, if larger string contained exact substring match).
                // Use relative match.
                // Example:
                //  Bob -> B0bWuzHere
                //  Percent match:    (10 - 8) / 10 = 20%
                //  Max poss match:   3 / 10        = 30%
                //  Relative percent: 20% / 30%     = 66.6%
                return (percent / maxPossMatch) * 100;

                // Another Algorithm works as follows:
                // Calc percent to which the string matched.
                // Calc largest possible match (i.e difference in length, if larger string contained exact substring match).
                // Assume 100% match, then remove the relative amount the strings didn't match.
                // Example:
                //  Bob -> B0bWuzHere
                //  Percent match:  (10 - 8) / 10      = 20%
                //  Max poss match: 3 / 10             = 30%
                //  Assume 100%:    100% - (30% - 20%) = 90%
                //return 100 - (maxPossMatch - percent) * 100;
            }
            /// <summary>Returns the number of changes needed to be made to the first string to turn it into the second string.</summary>
            /// <param name="s1">The first string.  Note: This string needs to be the longer of the two.</param>
            /// <param name="s2">The second string.  Note: This string needs to be the shorter of the two.</param>
            /// <returns>The number of changes needed to be made to the first string.</returns>
            public static Int32 calcLevenshteinDistance(String s1, String s2)
            {
                // [0] Length of S1.
                // [1] Length of S2.
                // [2] Where we're at in S1.
                // [3] Where we're at in S2.
                // [4] The Character at posS1 in S1.
                // [5] The Character at posS2 in S2.
                int lenS1, lenS2;
                int posS1, posS2;
                String chrS1, chrS2;

                // Exit early if one is an empty string.
                lenS1 = s1.Length;
                lenS2 = s2.Length;
                if (lenS1 == 0)
                    return lenS2;
                if (lenS2 == 0)
                    return lenS1;

                // Build a matrix.
                // Fill first row with 0,1,2,3,4,5...
                // Fill first column with 0,1,2,3,4,5...
                int[][] matrix = new int[lenS1][];
                for (int i = 0; i < lenS1; i++)
                    matrix[i] = new int[lenS2];

                for (int i = 0; i < lenS1; i++)
                    matrix[i][0] = i;

                for (int i = 0; i < lenS2; i++)
                    matrix[0][i] = i;


                // Start filling the matrix.
                // For each character in the first string...
                for (posS1 = 1; posS1 < lenS1; posS1++)
                {
                    chrS1 = s1.Substring(posS1, 1);

                    // For each character in the second string...
                    for (posS2 = 1; posS2 < lenS2; posS2++)
                    {
                        chrS2 = s2.Substring(posS2, 1);

                        // Determine if they are equal.
                        int cost = 0;
                        if (chrS1 != chrS2)
                            cost = 1;

                        // Calculate which value should be put in this spot of the matrix.
                        int val1 = matrix[posS1 - 1][posS2] + 1;
                        int val2 = matrix[posS1][posS2 - 1] + 1;
                        int val3 = matrix[posS1 - 1][posS2 - 1] + cost;
                        matrix[posS1][posS2] = Math.Min(Math.Min(val1, val2), val3);
                    }
                }

                // Return the number of additions/removals/changes it would take for the first string to match the second string.
                return matrix[lenS1 - 1][lenS2 - 1];
            }
        }
        /// <summary>Holds an action to perform along with it's arguments.</summary>
        public class ActionEvent
        {
            private Commands command = 0;
            private Int32 argsIndex = 0;
            private List<Object> args = new List<Object>();

            public Commands Command { get { return command; } }
            public Object Argument { get { return args[(argsIndex == args.Count) ? (argsIndex = 1) - 1 : argsIndex++]; } }

            /// <summary>Takes all the parameters for an event and creates them as neccesary.</summary>
            public ActionEvent(Commands command, Object[] args)
            {
                this.command = command;
                foreach (Object arg in args)
                    this.args.Add(arg);
            }
        }



        /// <summary>Allows PRoCon to get the name of this plugin.</summary>
        public string GetPluginName() { return "Teamspeak 3 Sync"; }
        /// <summary>Allows PRoCon to get the version of this plugin.</summary>
        public string GetPluginVersion() { return "1.0.0.0 PURE"; }
        /// <summary>Allows PRoCon to get the author's name of this plugin.</summary>
        public string GetPluginAuthor() { return "Imisnew2"; }
        /// <summary>Allows PRoCon to get the website for this plugin.</summary>
        public string GetPluginWebsite() { return "www.TeamPlayerGaming.com/members/Imisnew2.html"; }
        /// <summary>Allows ProCon to get a description of this plugin.</summary>
        public string GetPluginDescription()
        {
            return "<h2>Description</h2>" +
                       "<p>Teamspeak 3 Sync synchronizes your Game Server with a Teamspeak 3 Server, moving players into their respective channels on Teamspeak 3 dependent on the player's team in the Game Server.<br/>" +
                          "The plugin also allows you to 'Message' at players who are not in your Teamspeak 3 Server, yet playing on your Game Server.</p>" +

                    "<h2>Setup Instructions</h2>" +

                       "<h3>PRoCon Setup:</h3>" +
                          "<p>Teamspeak 3 Sync doesn't need any special setup out-of-the-box unless PRoCon is running in a sandbox.  You can check whether PRoCon is running in a sandbox and add exceptions for Teamspeak 3 Sync by following the steps outlined below:</p>" +
                          "<ul style=\"margin-top: 0px;\">" +
                             "<li>Start PRoCon, go to Tools &gt; Options.</li>" +
                             "<li>Under the Plugins tab, check if the drop-down is set to \"Run plugins in a sandbox (recommended).\"</li>" +
                             "<li>If it is, add your Teamspeak 3 Server's IP and Query Port to the Trusted Domains.</li>" +
                          "</ul>" +

                       "<h3>Teamspeak Setup:</h3>" +
                          "<p>While the Teamspeak Server doesn't require any special setup, the plugin may be banned occasionally due to sending commands too quickly.  For this reason, I've outlined how to get around this issue:</p>" +
                          "<ul style=\"margin-top: 0px;\">" +
                             "<li>Add your PRoCon's IP to query_ip_whitelist.txt.  This is found in the root folder of your Teamspeak 3 Server.  If your Teamspeak 3 Server is being hosted by a server provider, ask your provider to do this for you.</li>" +
                             "<li>Setup your ServerQuery Login through Teamspeak by going to <b>Tools -> ServerQuery Login -> *Enter A Username* -> *Receive Auto Generated Password*</b>.</li>" +
                             "<li>To find what port your Teamspeak 3 Server uses as its <i>query port</i>, look in the server.ini file.  The default value is 10011.  If your Teamspeak 3 Server is being hosted by a server provider, ask your provider for the query port.</li>" +
                          "</ul>" +

                     "<h2>Settings</h2>" +
                       "<p>I've tried to let the user control every aspect of the program, while still leaving the interface easy to use.  Below, you'll find descriptions of each of the plugin's various settings.</p>" +

                       "<h3>Section 1 - Teamspeak 3</h3>" +
                         "<h4>Server IP</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The IP of your Teamspeak 3 Server you wish to synchronize with a Game Server." +
                           "<br/><u>Note</u>: An IP, such as 127.0.0.1, or a Hostname, such as ts3.myteamspeakserver.com." +
                         "</blockquote>" +

                         "<h4>Server Port</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The Port of your Teamspeak 3 Server that clients normally use to connect to your server." +
                           "<br/><u>Note</u>: Normally 9987, but can be any number between 0 and 65535, inclusive." +
                         "</blockquote>" +

                         "<h4>Query Port</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The Port of your Teamspeak 3 Server that Server Query clients use to connect to your server." +
                           "<br/><u>Note</u>: Normally 10011, but can be any number between 0 and 65535, inclusive." +
                         "</blockquote>" +

                         "<h4>Query Username</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The username of your Server Query login." +
                         "</blockquote>" +

                         "<h4>Query Password</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The password of your Server Query login." +
                         "</blockquote>" +

                         "<h4>Staging Channel Name</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The name of the channel you wish to use as the Staging Channel for Teamspeak 3 Sync." +
                           "<br/><u>Note</u>: This channel must exist prior to starting Teamspeak 3 Sync." +
                         "</blockquote>" +

                         "<h4>Enable Dropoff Channel</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Enable this option if you want the staging channel to only be used for between-round chat only.  Players who leave the game will be moved to the Dropoff Channel." +
                           "<br/><u>Note</u>: This channel must exist prior to starting Teamspeak 3 Sync." +
                         "</blockquote>" +

                         "<h4>Dropoff Channel Name</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The name of the channel you wish to move players to after they leave the game (means the Staging channel is used for between-round chat only)." +
                           "<br/><u>Note</u>: This channel must exist prior to starting Teamspeak 3 Sync." +
                         "</blockquote>" +

                         "<h4>Pickup Channel Names</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The name of channels you wish to allow Teamspeak 3 Sync to pull clients from." +
                           "<br/><u>Note</u>: The Staging Channel is included in this list by default.  These channels must exist when Teamspeak 3 Sync is enabled in order for the channels to be used." +
                         "</blockquote>" +

                       "<h3>Section 2 - Channels</h3>" +
                         "<h4>Password</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The password that will be placed on channels created by Teamspeak 3 Sync." +
                           "<br/><u>Note</u>: Can be left blank to specify \"no password.\"" +
                         "</blockquote>" +

                         "<h4>Team Names</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "These names will be used when team channels are searched for or created by Teamspeak 3 Sync." +
                           "<br/><u>Note</u>: Teamspeak 3 Sync will only search for team channels that are under the root channel, or the staging channel.  If a team that doesn't have a name specified is attempted to be created, it will default to \"Team #.\"" +
                         "</blockquote>" +

                         "<h4>Squad Names</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "These names will be used when squad channels are created by Teamspeak 3 Sync." +
                           "<br/><u>Note</u>: Teamspeak 3 Sync will only search for squad channels that are under team channels.  If a squad that doesn't have a name specified is attempted to be created, it will default to \"Squad #.\"" +
                         "</blockquote>" +

                         "<h4>Remove When Empty</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Removes channels as they become empty." +
                           "<br/><u>Note</u>: This will slow down Teamspeak 3 Sync, as it takes a little bit of time to remove and create channels.  Does not remove team channels that are placed under the root channel." +
                         "</blockquote>" +

                       "<h3>Section 3 - Synchronization</h3>" +
                         "<h4>Delay Queries</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls whether queries are forced to wait between sending queries." +
                           "<br/><u>Note</u>: This setting is intended for users who are not allowed to alter their query_ip_whitelist.txt file.  However, for a server that receives an intense amount of traffic, you will still be banned for flooding occasionally." +
                         "</blockquote>" +

                         "<h4>Delay Queries Amount (ms)</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls the amount of time, in milliseconds, that queries must wait before sending another." +
                           "<br/><u>Note</u>: By default, Teamspeak 3 bans Server Query clients who send commands at a rate of 10 per 3 seconds.  However, during testing, a 500ms+ delay was the only reliable amount to avoid being banned repeatedly.  Can be set to any number between 10 and 3000, inclusive." +
                         "</blockquote>" +

                         "<h4>Update Interval (ms)</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls the amount of time, in milliseconds, between requesting an update from the Teamspeak 3 Server." +
                           "<br/><u>Note</u>: By default, this is set to 10000, however, it can be set to any number between 4000 and 60000, inclusive." +
                         "</blockquote>" +

                         "<h4>Team-Based Swapping</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls whether players are split into teams." +
                           "<br/><u>Note</u>: Turning this off leaves everyone in the staging channel." +
                         "</blockquote>" +

                         "<h4>Team-Based Threshold</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The number of players that must be on both the Teamspeak 3 Server and Game Server before Team-Based swapping will begin." +
                           "<br/><u>Note</u>: Setting this to 1 will ensure Team-Based swapping is always on.  Can not be set above 32." +
                         "</blockquote>" +

                         "<h4>Intermission Swapping</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls whether players moved to the staging channel between rounds." +
                           "<br/><u>Note</u>: Turning this off leaves everyone in their current channel between rounds." +
                         "</blockquote>" +

                         "<h4>Squad-Based Swapping</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls whether players are split into squads." +
                           "<br/><u>Note</u>: Turning this off leaves everyone in their respective team channels." +
                         "</blockquote>" +

                         "<h4>Squad-Based Threshold</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The number of players that must be on both the Teamspeak 3 Server and Game Server, per team, before Squad-Based swapping will begin." +
                           "<br/><u>Note</u>: Setting this and Squad-Size Minimum to 1 will ensure Squad-Based swapping is always on.  Can not be set above 32." +
                         "</blockquote>" +

                         "<h4>Squad Minimum</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The number of players that must be on both the Teamspeak 3 Server and Game Server, per team, per squad, before Squad-Based swapping will begin." +
                         "</blockquote>" +

                         "<h4>Remove Clients Not Playing</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls whether players who are in Teamspeak 3, but not the Game Server, will be removed from Team and Squad channels." +
                           "<br/><u>Note</u>: Leaving this off will allow others to stay in channels when they are not playing in the Game Server." +
                         "</blockquote>" +

                         "<h4>Remove Clients - Whitelist</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The Database Ids of players you wish to be able to remain in Team and Squad channels when not in the Game Server." +
                           "<br/><u>Note</u>: This only has an effect if \"Remove Clients Not Playing\" is enabled." +
                         "</blockquote>" +

                       "<h3>Section 4 - Error Handling</h3>" +
                         "<h4>Reconnect On Error</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls whether Teamspeak 3 Sync will attempt to reconnect to the Teamspeak 3 Server, should a network issue arise." +
                           "<br/><u>Note</u>: Does not attempt to reconnect after certain errors, such as, a bad login." +
                         "</blockquote>" +

                         "<h4>Reconnect On Error Attempts</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The number of times Teamspeak 3 Sync will attempt to establish a connection before giving up." +
                           "<br/><u>Note</u>: Can be set to any number between 1 and 60, inclusive." +
                         "</blockquote>" +

                         "<h4>Interval Between Reconnect Attempts (ms)</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls the amount of time, in milliseconds, between each reconnect attempt." +
                           "<br/><u>Note</u>: Can be set to any number between 1000 and 15000, inclusive." +
                         "</blockquote>" +

                       "<h3>Section 5 - User Messages</h3>" +
                         "<h4>Message Players Not In Teamspeak</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls whether Teamspeak 3 Sync will message players on the Game Server that are not in Teamspeak 3." +
                           "<br/><u>Note</u>: Does not work with Battlefield 3." +
                         "</blockquote>" +

                         "<h4>Message When Player Joins</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls whether Teamspeak 3 Sync will message a player after they join the Game Server." +
                           "<br/><u>Note</u>: Does not work with Battlefield 3." +
                         "</blockquote>" +

                         "<h4>Message When Player Joins Delay (ms)</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls the time, in milliseconds, that Teamspeak 3 Sync waits before sending a message to a player after they have joined." +
                           "<br/><u>Note</u>: Can be set to any number between 0 and 1800000, inclusive." +
                         "</blockquote>" +

                         "<h4>Message Interval (ms)</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls the time, in milliseconds, that Teamspeak 3 Sync mass messages players." +
                           "<br/><u>Note</u>: Can be set to any number between the Message Display Duration and 3600000, inclusive." +
                         "</blockquote>" +

                         "<h4>Message</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The message to be sent to players." +
                           "<br/><u>Note</u>: Usually notifies players of the Teamspeak 3 Server being used for Teamspeak 3 Sync." +
                         "</blockquote>" +

                         "<h4>Message Display Duration (ms)</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Controls the time, in milliseconds, that the message is displayed on the player's screen." +
                           "<br/><u>Note</u>: Can be set to any number between the 1000 and 30000, inclusive." +
                         "</blockquote>" +

                       "<h3>Section 6 - In-Game Commands</h3>" +
                         "<h4>Enable !tssquads Command</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Enables a command where !tssquads will list teamspeak squads with less than 4 players." +
                         "</blockquote>" +

                         "<h4>Enable !tslobby Command</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Determines whether the server will acknowledge the !tslobby command which will keep the player in the staging channel until !tssync is issued or the round ends." +
                         "</blockquote>" +

                         "<h4>Enable !tsteam Command</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Determines whether the server will acknowledge the !tsteam command which will keep the player in the team channel until !tssync is issued or the round ends." +
                         "</blockquote>" +

                         "<h4>Enable !tsnosync Command</h4>" +
                         "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Determines whether the server will acknowledge the !tsnosync command which will tell the server to ignore swapping the player until !tssync is issued or the round ends." +
                           "<br/><u>Note</u>: This setting can be useful for administrators who wish to speak with someone in-game." +
                         "</blockquote>" +

                       "<h3>Section 7 - Debug Information</h3>" +
                         "<p>This section only contains controls relevant to displaying extra information related to the plugin's inner operations.</p>" +

                        "<h3> Section 8 - Event Bouncer</h3>" +
                            "<p>This section contains controls relevant to the custom Event Bouncer functionality</p>" +

                        "<h4>Enable Event Bouncer</h4>" +
                           "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "Determines whether players not in the managed Teamspeak channels will be kicked when spawning." +
                         "</blockquote>" +

                         "<h4>Event Bouncer Kick Message</h4>" +
                           "<blockquote style=\"margin-left: 0px; margin-right:0px; margin-top:0px;\">" +
                           "The message shown to players kicked by the Event Bouncer." +
                         "</blockquote>";

        }



        /// <summary>Allows PRoCon to figure out what fields to display to the user.</summary>
        public List<CPluginVariable> GetDisplayPluginVariables()
        {
            List<CPluginVariable> lstReturn = new List<CPluginVariable>();

            // -- Section 1 - Teamspeak 3 -----------------------------------------
            lstReturn.Add(new CPluginVariable("Section 1 - Teamspeak 3|Server IP", typeof(String), ts3ServerIp));
            lstReturn.Add(new CPluginVariable("Section 1 - Teamspeak 3|Server Port", typeof(Int32), ts3ServerPort));
            lstReturn.Add(new CPluginVariable("Section 1 - Teamspeak 3|Query Port", typeof(Int32), ts3QueryPort));
            lstReturn.Add(new CPluginVariable("Section 1 - Teamspeak 3|Query Username", typeof(String), ts3QueryUsername));
            lstReturn.Add(new CPluginVariable("Section 1 - Teamspeak 3|Query Password", typeof(String), ts3QueryPassword));
            lstReturn.Add(new CPluginVariable("Section 1 - Teamspeak 3|Query Nickname", typeof(String), ts3QueryNickname));
            lstReturn.Add(new CPluginVariable("Section 1 - Teamspeak 3|Staging Channel Name", typeof(String), ts3StgChannelName));
            lstReturn.Add(new CPluginVariable("Section 1 - Teamspeak 3|Enable Dropoff Channel", typeof(Boolean), ts3EnableDropoff));
            lstReturn.Add(new CPluginVariable("Section 1 - Teamspeak 3|Dropoff Channel Name", typeof(String), ts3DropoffChannelName));
            lstReturn.Add(new CPluginVariable("Section 1 - Teamspeak 3|Pickup Channel Names", typeof(String[]), ts3PckChannelNames));

            // -- Section 2 - Channels --------------------------------------------
            lstReturn.Add(new CPluginVariable("Section 2 - Channels|Password", typeof(String), chnPassword));
            lstReturn.Add(new CPluginVariable("Section 2 - Channels|Team Names", typeof(String[]), chnTeamNames));
            lstReturn.Add(new CPluginVariable("Section 2 - Channels|Squad Names", typeof(String[]), chnSquadNames));
            lstReturn.Add(new CPluginVariable("Section 2 - Channels|Remove When Empty", typeof(Boolean), chnRemoveOnEmpty));

            // -- Section 3 - Synchronization -------------------------------------
            lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Delay Queries", typeof(Boolean), synDelayQueries));
            if (synDelayQueries)
                lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Delay Queries Amount (ms)", typeof(Int32), synDelayQueriesAmount));
            lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Update Interval (ms)", typeof(Int32), synUpdateInterval));
            lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Team-Based Swapping", typeof(Boolean), synTeamBasedSwapping));
            if (synTeamBasedSwapping)
            {
                lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Team-Based Threshold", typeof(Int32), synTeamBasedThreshold));
                lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Intermission Swapping", typeof(Boolean), synIntermissionSwapping));
                lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Squad-Based Swapping", typeof(Boolean), synSquadBasedSwapping));
                if (synSquadBasedSwapping)
                {
                    lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Squad-Based Threshold", typeof(Int32), synSquadBasedThreshold));
                    lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Squad-Size Minimum", typeof(Int32), synSquadSizeMinimum));
                }
            }
            // lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Matching Threshold (%)",         typeof(Double),  synMatchingThreshold));
            lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Remove Clients Not Playing", typeof(Boolean), synRemoveClients));
            if (synRemoveClients)
                lstReturn.Add(new CPluginVariable("Section 3 - Synchronization|Remove Clients - Whitelist", typeof(String[]), synRemoveClientsWhitelist));

            // -- Section 4 - Error Handling --------------------------------------
            lstReturn.Add(new CPluginVariable("Section 4 - Error Handling|Reconnect On Error", typeof(Boolean), errReconnectOnError));
            if (errReconnectOnError)
            {
                lstReturn.Add(new CPluginVariable("Section 4 - Error Handling|Number of Reconnect Attempts", typeof(Int32), errReconnectOnErrorAttempts));
                lstReturn.Add(new CPluginVariable("Section 4 - Error Handling|Interval Between Reconnect Attempts (ms)", typeof(Int32), errReconnectOnErrorInterval));
            }

            // -- Section 5 - User Messages ---------------------------------------
            lstReturn.Add(new CPluginVariable("Section 5 - User Messages|Message Players Not In Teamspeak", typeof(Boolean), msgEnabled));
            if (msgEnabled)
            {
                lstReturn.Add(new CPluginVariable("Section 5 - User Messages|Message When Player Joins", typeof(Boolean), msgOnJoin));
                if (msgOnJoin)
                    lstReturn.Add(new CPluginVariable("Section 5 - User Messages|Message When Player Joins Delay (ms)", typeof(Int32), msgOnJoinDelay));
                lstReturn.Add(new CPluginVariable("Section 5 - User Messages|Message Interval (ms)", typeof(Int32), msgInterval));
                lstReturn.Add(new CPluginVariable("Section 5 - User Messages|Message", typeof(String), msgMessage));
                lstReturn.Add(new CPluginVariable("Section 5 - User Messages|Message Display Duration", typeof(Int32), msgDuration));
            }
            // -- Section 6 - Debug Information -----------------------------------
            lstReturn.Add(new CPluginVariable("Section 6 - In-Game Commands|Enable !tssquads",                       typeof(Boolean), mEnableTSSquadList));
            lstReturn.Add(new CPluginVariable("Section 6 - In-Game Commands|Enable !tslobby",                         typeof(Boolean), mEnableTSStaging));
            lstReturn.Add(new CPluginVariable("Section 6 - In-Game Commands|Enable !tsteam",                            typeof(Boolean), mEnableTSTeam));
            lstReturn.Add(new CPluginVariable("Section 6 - In-Game Commands|Enable !tsnosync",                          typeof(Boolean), mEnableTSNoSync));
            // -- Section 7 - Debug Information -----------------------------------
            lstReturn.Add(new CPluginVariable("Section 7 - Debug Information|Show Debug Messages (Events)",   typeof(Boolean), dbgEvents));
            lstReturn.Add(new CPluginVariable("Section 7 - Debug Information|Show Debug Messages (Clients)",  typeof(Boolean), dbgClients));
            lstReturn.Add(new CPluginVariable("Section 7 - Debug Information|Show Debug Messages (Channels)", typeof(Boolean), dbgChannels));
            lstReturn.Add(new CPluginVariable("Section 7 - Debug Information|Show Debug Messages (Swapping)", typeof(Boolean), dbgSwapping));
            lstReturn.Add(new CPluginVariable("Section 7 - Debug Information|Show Debug Messages (Network)",  typeof(Boolean), dbgNetwork));
            lstReturn.Add(new CPluginVariable("Section 7 - Debug Information|Show Debug Messages (Bouncer)", typeof(Boolean), dbgBouncer));

            // -- Section 8 - Event Bouncer ---------------------------------------
            lstReturn.Add(new CPluginVariable("Section 8 - Event Bouncer|Enable Event Bouncer", typeof(Boolean), mEnableBouncer));
            lstReturn.Add(new CPluginVariable("Section 8 - Event Bouncer|Event Bouncer Kick Message", typeof(String), mBouncerKickMessage));

            return lstReturn;
        }
        /// <summary>Allows PRoCon to save the variables for persistence across sessions.</summary>
        public List<CPluginVariable> GetPluginVariables()
        {
            List<CPluginVariable> lstReturn = new List<CPluginVariable>();

            // -- Section 1 - Teamspeak 3 -----------------------------------------
            lstReturn.Add(new CPluginVariable("Server IP", typeof(String), ts3ServerIp));
            lstReturn.Add(new CPluginVariable("Server Port", typeof(Int32), ts3ServerPort));
            lstReturn.Add(new CPluginVariable("Query Port", typeof(Int32), ts3QueryPort));
            lstReturn.Add(new CPluginVariable("Query Username", typeof(String), ts3QueryUsername));
            lstReturn.Add(new CPluginVariable("Query Password", typeof(String), ts3QueryPassword));
            lstReturn.Add(new CPluginVariable("Query Nickname", typeof(String), ts3QueryNickname));
            lstReturn.Add(new CPluginVariable("Staging Channel Name", typeof(String), ts3StgChannelName));
            lstReturn.Add(new CPluginVariable("Enable Dropoff Channel", typeof(Boolean), ts3EnableDropoff));
            lstReturn.Add(new CPluginVariable("Dropoff Channel Name", typeof(String), ts3DropoffChannelName));
            lstReturn.Add(new CPluginVariable("Pickup Channel Names", typeof(String[]), ts3PckChannelNames));
            // -- Section 2 - Channels --------------------------------------------
            lstReturn.Add(new CPluginVariable("Password", typeof(String), chnPassword));
            lstReturn.Add(new CPluginVariable("Team Names", typeof(String[]), chnTeamNames));
            lstReturn.Add(new CPluginVariable("Squad Names", typeof(String[]), chnSquadNames));
            lstReturn.Add(new CPluginVariable("Remove When Empty", typeof(Boolean), chnRemoveOnEmpty));
            // -- Section 3 - Synchronization -------------------------------------
            lstReturn.Add(new CPluginVariable("Delay Queries", typeof(Boolean), synDelayQueries));
            lstReturn.Add(new CPluginVariable("Delay Queries Amount (ms)", typeof(Int32), synDelayQueriesAmount));
            lstReturn.Add(new CPluginVariable("Update Interval (ms)", typeof(Int32), synUpdateInterval));
            lstReturn.Add(new CPluginVariable("Team-Based Swapping", typeof(Boolean), synTeamBasedSwapping));
            lstReturn.Add(new CPluginVariable("Team-Based Threshold", typeof(Int32), synTeamBasedThreshold));
            lstReturn.Add(new CPluginVariable("Intermission Swapping", typeof(Boolean), synIntermissionSwapping));
            lstReturn.Add(new CPluginVariable("Squad-Based Swapping", typeof(Boolean), synSquadBasedSwapping));
            lstReturn.Add(new CPluginVariable("Squad-Based Threshold", typeof(Int32), synSquadBasedThreshold));
            lstReturn.Add(new CPluginVariable("Squad-Size Minimum", typeof(Int32), synSquadSizeMinimum));
            lstReturn.Add(new CPluginVariable("Matching Threshold (%)", typeof(Double), synMatchingThreshold));
            lstReturn.Add(new CPluginVariable("Remove Clients Not Playing", typeof(Boolean), synRemoveClients));
            lstReturn.Add(new CPluginVariable("Remove Clients - Whitelist", typeof(String[]), synRemoveClientsWhitelist));
            // -- Section 4 - Error Handling --------------------------------------
            lstReturn.Add(new CPluginVariable("Reconnect On Error", typeof(Boolean), errReconnectOnError));
            lstReturn.Add(new CPluginVariable("Number of Reconnect Attempts", typeof(Int32), errReconnectOnErrorAttempts));
            lstReturn.Add(new CPluginVariable("Interval Between Reconnect Attempts (ms)", typeof(Int32), errReconnectOnErrorInterval));
            // -- Section 5 - User Messages ---------------------------------------
            lstReturn.Add(new CPluginVariable("Message Players Not In Teamspeak", typeof(Boolean), msgEnabled));
            lstReturn.Add(new CPluginVariable("Message When Player Joins", typeof(Boolean), msgOnJoin));
            lstReturn.Add(new CPluginVariable("Message When Player Joins Delay (ms)", typeof(Int32), msgOnJoinDelay));
            lstReturn.Add(new CPluginVariable("Message Interval (ms)", typeof(Int32), msgInterval));
            lstReturn.Add(new CPluginVariable("Message", typeof(String), msgMessage));
            lstReturn.Add(new CPluginVariable("Message Display Duration", typeof(Int32), msgDuration));
            // -- Section 6 - Debug Information -----------------------------------
            lstReturn.Add(new CPluginVariable("Enable !tssquads", typeof(Boolean), mEnableTSSquadList));
            lstReturn.Add(new CPluginVariable("Enable !tslobby", typeof(Boolean), mEnableTSStaging));
            lstReturn.Add(new CPluginVariable("Enable !tsteam", typeof(Boolean), mEnableTSTeam));
            lstReturn.Add(new CPluginVariable("Enable !tsnosync", typeof(Boolean), mEnableTSNoSync));
            // -- Section 7 - Debug Information -----------------------------------
            lstReturn.Add(new CPluginVariable("Show Debug Messages (Events)", typeof(Boolean), dbgEvents));
            lstReturn.Add(new CPluginVariable("Show Debug Messages (Clients)", typeof(Boolean), dbgClients));
            lstReturn.Add(new CPluginVariable("Show Debug Messages (Channels)", typeof(Boolean), dbgChannels));
            lstReturn.Add(new CPluginVariable("Show Debug Messages (Swapping)", typeof(Boolean), dbgSwapping));
            lstReturn.Add(new CPluginVariable("Show Debug Messages (Network)", typeof(Boolean), dbgNetwork));
            lstReturn.Add(new CPluginVariable("Show Debug Messages (Bouncer)", typeof(Boolean), dbgBouncer));
            // -- Section 8 - Event Bouncer ---------------------------------------
            lstReturn.Add(new CPluginVariable("Enable Event Bouncer", typeof(Boolean), mEnableBouncer));
            lstReturn.Add(new CPluginVariable("Event Bouncer Kick Message", typeof(String), mBouncerKickMessage));

            return lstReturn;
        }
        /// <summary>Allows PRoCon to load the variables from a previous session.  Also allows the user to change the variable as well.</summary>
        public void SetPluginVariable(String strVariable, String strValue)
        {
            //Temporary out variable for TryParse methods.
            UInt16 ushtOut = 0;
            Int32 intOut = 0;
            Double dblOut = 0;
            Boolean blnOut = false;

            // -- Section 1 - Teamspeak 3 -----------------------------------------
            if (strVariable == "Server IP")
                ts3ServerIp = strValue;
            else if (strVariable == "Server Port" && UInt16.TryParse(strValue, out ushtOut))
                ts3ServerPort = ushtOut;
            else if (strVariable == "Query Port" && UInt16.TryParse(strValue, out ushtOut))
                ts3QueryPort = ushtOut;
            else if (strVariable == "Query Username")
                ts3QueryUsername = strValue;
            else if (strVariable == "Query Password")
                ts3QueryPassword = strValue;
            else if (strVariable == "Query Nickname")
                ts3QueryNickname = strValue;
            else if (strVariable == "Staging Channel Name")
                ts3StgChannelName = strValue;
            else if (strVariable == "Enable Dropoff Channel" && Boolean.TryParse(strValue, out blnOut))
                ts3EnableDropoff = blnOut;
            else if (strVariable == "Dropoff Channel Name")
                ts3DropoffChannelName = strValue;
            else if (strVariable == "Pickup Channel Names")
                ts3PckChannelNames = CPluginVariable.DecodeStringArray(strValue);

            // -- Section 2 - Channels --------------------------------------------
            else if (strVariable == "Password")
                chnPassword = strValue.Trim();
            else if (strVariable == "Team Names")
                chnTeamNames = CPluginVariable.DecodeStringArray(strValue);
            else if (strVariable == "Squad Names")
                chnSquadNames = CPluginVariable.DecodeStringArray(strValue);
            else if (strVariable == "Remove When Empty" && Boolean.TryParse(strValue, out blnOut))
                chnRemoveOnEmpty = blnOut;

            // -- Section 3 - Synchronization -------------------------------------
            else if (strVariable == "Delay Queries" && Boolean.TryParse(strValue, out blnOut))
                synDelayQueries = blnOut;
            else if (strVariable == "Delay Queries Amount (ms)" && Int32.TryParse(strValue, out intOut))
                synDelayQueriesAmount = (intOut >= 10 && intOut <= 3000) ? intOut : synDelayQueriesAmount;
            else if (strVariable == "Update Interval (ms)" && Int32.TryParse(strValue, out intOut))
                synUpdateInterval = (intOut >= 4000 && intOut <= 60000) ? intOut : synUpdateInterval;
            else if (strVariable == "Team-Based Swapping" && Boolean.TryParse(strValue, out blnOut))
                synTeamBasedSwapping = blnOut;
            else if (strVariable == "Team-Based Threshold" && Int32.TryParse(strValue, out intOut))
                synTeamBasedThreshold = (intOut >= 1 && intOut <= 32) ? intOut : synTeamBasedThreshold;
            else if (strVariable == "Intermission Swapping" && Boolean.TryParse(strValue, out blnOut))
                synIntermissionSwapping = blnOut;
            else if (strVariable == "Squad-Based Swapping" && Boolean.TryParse(strValue, out blnOut))
                synSquadBasedSwapping = blnOut;
            else if (strVariable == "Squad-Based Threshold" && Int32.TryParse(strValue, out intOut))
                synSquadBasedThreshold = (intOut >= 1 && intOut <= 32) ? intOut : synSquadBasedThreshold;
            else if (strVariable == "Squad-Size Minimum" && Int32.TryParse(strValue, out intOut))
                synSquadSizeMinimum = (intOut >= 1 && intOut <= 6) ? intOut : synSquadSizeMinimum;
            else if (strVariable == "Matching Threshold (%)" && Double.TryParse(strValue, out dblOut))
                synMatchingThreshold = 100; //(dblOut >= 0.0 && dblOut <= 100.0) ? dblOut : synMatchingThreshold;
            else if (strVariable == "Remove Clients Not Playing" && Boolean.TryParse(strValue, out blnOut))
                synRemoveClients = blnOut;
            else if (strVariable == "Remove Clients - Whitelist")
            {
                mClientWhitelist.Clear();
                synRemoveClientsWhitelist = CPluginVariable.DecodeStringArray(strValue);
                foreach (String id in synRemoveClientsWhitelist)
                    if (Int32.TryParse(id, out intOut) && !mClientWhitelist.Contains(intOut))
                        mClientWhitelist.Add(intOut);
            }

            // -- Section 4 - Error Handling --------------------------------------
            else if (strVariable == "Reconnect On Error" && Boolean.TryParse(strValue, out blnOut))
                errReconnectOnError = blnOut;
            else if (strVariable == "Number of Reconnect Attempts" && Int32.TryParse(strValue, out intOut))
                errReconnectOnErrorAttempts = (intOut >= 1) ? intOut : errReconnectOnErrorAttempts;
            else if (strVariable == "Interval Between Reconnect Attempts (ms)" && Int32.TryParse(strValue, out intOut))
                errReconnectOnErrorInterval = (intOut >= 1000) ? intOut : errReconnectOnErrorInterval;

            // -- Section 5 - User Messages ---------------------------------------
            else if (strVariable == "Message Players Not In Teamspeak" && Boolean.TryParse(strValue, out blnOut))
                msgEnabled = blnOut;
            else if (strVariable == "Message When Player Joins" && Boolean.TryParse(strValue, out blnOut))
                msgOnJoin = blnOut;
            else if (strVariable == "Message When Player Joins Delay (ms)" && Int32.TryParse(strValue, out intOut))
                msgOnJoinDelay = (intOut >= 0) ? intOut : msgOnJoinDelay;
            else if (strVariable == "Message Interval (ms)" && Int32.TryParse(strValue, out intOut))
                msgInterval = (intOut >= 30000) ? intOut : msgInterval;
            else if (strVariable == "Message")
                msgMessage = strValue;
            else if (strVariable == "Message Display Duration" && Int32.TryParse(strValue, out intOut))
                msgDuration = (intOut >= 0) ? intOut : msgDuration;
            // -- Section 6 - In-Game Commands -----------------------------------
            else if (strVariable == "Enable !tssquads" && Boolean.TryParse(strValue, out blnOut))
                mEnableTSSquadList = blnOut;
            else if (strVariable == "Enable !tslobby" && Boolean.TryParse(strValue, out blnOut))
                mEnableTSStaging = blnOut;
            else if (strVariable == "Enable !tsteam" && Boolean.TryParse(strValue, out blnOut))
                mEnableTSTeam = blnOut;
            else if (strVariable == "Enable !tsnosync" && Boolean.TryParse(strValue, out blnOut))
                mEnableTSNoSync = blnOut;
            // -- Section 7 - Debug Information -----------------------------------
            else if (strVariable == "Show Debug Messages (Events)" && Boolean.TryParse(strValue, out blnOut))
                dbgEvents = blnOut;
            else if (strVariable == "Show Debug Messages (Clients)" && Boolean.TryParse(strValue, out blnOut))
                dbgClients = blnOut;
            else if (strVariable == "Show Debug Messages (Channels)" && Boolean.TryParse(strValue, out blnOut))
                dbgChannels = blnOut;
            else if (strVariable == "Show Debug Messages (Swapping)" && Boolean.TryParse(strValue, out blnOut))
                dbgSwapping = blnOut;
            else if (strVariable == "Show Debug Messages (Network)" && Boolean.TryParse(strValue, out blnOut))
                dbgNetwork = blnOut;
            else if (strVariable == "Show Debug Messages (Bouncer)" && Boolean.TryParse(strValue, out blnOut))
                dbgBouncer = blnOut;

            // -- Section 8 - Event Bouncer --------------------------------------
            else if (strVariable == "Enable Event Bouncer" && Boolean.TryParse(strValue, out blnOut))
                mEnableBouncer = blnOut;
            else if (strVariable == "Event Bouncer Kick Message")
                mBouncerKickMessage = strValue;
        }



        /// <summary>Is called when the plugin is successfully loaded.</summary>
        public void OnPluginLoaded(String strHostName, String strPort, String strPRoConVersion)
        {
            // Register Events.
            this.RegisterEvents("OnPlayerJoin", "OnPlayerLeft", "OnPlayerSpawned", "OnPlayerTeamChange", "OnPlayerSquadChange", "OnListPlayers", "OnPunkbusterPlayerInfo", "OnLevelLoaded", "OnRoundOver", "OnGlobalChat", "OnTeamChat", "OnSquadChat");

            // Create thread so UI doesn't hang up on networking.
            try
            {
                mThreadMain = new Thread(EntryMain);
                mThreadMessage = new Thread(EntryMessaging);
                mThreadSynchronize = new Thread(EntrySynchronization);
                mThreadMain.Start();
                mThreadMessage.Start();
                mThreadSynchronize.Start();
            }
            catch (Exception e)
            {
                consoleWrite("^8A fatal error occurred on load! Procon must be restarted for Teamspeak 3 Sync to work correctly.");
                consoleWrite("^8^bMessage:^n^0 " + e.Message);
                consoleWrite("^8^bStack Trace:^n^0 " + e.StackTrace);
            }
        }
        /// <summary>Starts the action queue.</summary>
        public void OnPluginEnable()
        {
            consoleWrite("[Enabled] ^2^bRequesting Teamspeak 3 Sync to start...^n");
            addToActionQueue(Commands.PluginEnabled);
        }
        /// <summary>Stops the action queue.</summary>
        public void OnPluginDisable()
        {
            consoleWrite("[Disabled] ^8^bRequesting Teamspeak 3 Sync to stop...^n");
            addToActionQueue(Commands.PluginDisabled);
            if (mTsReconnecting)
                mTsReconnEvent.Set();
        }



        /// <summary>Code-driven enabling or disabling of the plugin.</summary>
        public void setPluginState(Boolean state)
        {
            if (!mTsReconnecting) this.ExecuteCommand("procon.protected.plugins.enable", "TeamspeakSync", state.ToString());
        }
        /// <summary>Writes a message to the console with the "Ts3 Sync:" prefix.</summary>
        public void consoleWrite(String format, params Object[] args)
        {
            this.ExecuteCommand("procon.protected.pluginconsole.write", String.Format("Ts3 Sync: " + format, args));
        }
        /// <summary>Writes a message to the console with the "Ts3 Sync - Debug:" prefix.</summary>
        public void debugWrite(Boolean debug, String format, params Object[] args)
        {
            if (debug) this.ExecuteCommand("procon.protected.pluginconsole.write", String.Format("^7Ts3 Sync (Debug): " + format, args));
        }
        /// <summary>Sends a full-screen message to a player in the game.</summary>
        public void yellToPlayer(String message, Int32 duration, String player)
        {
            this.ExecuteCommand("procon.protected.send", "admin.yell", message, duration.ToString(), "player", player);
        }
        /// <summary>Sends a chat message to a player in the game.</summary>
        public void sayToPlayer(String message, String player)
        {
            this.ExecuteCommand("procon.protected.send", "admin.say", message, "player", player);
        }
        /// <summary>Requests an entire punkbuster list be sent.</summary>
        public void forcePbListing()
        {
            this.ExecuteCommand("procon.protected.send", "punkBuster.pb_sv_command", "pb_sv_plist");
        }
        /// <summary>Requests an entire player list be sent.</summary>
        public void forceGmListing()
        {
            this.ExecuteCommand("procon.protected.send", "admin.listPlayers", "all");
        }

        /// <summary>Is called when a player spawns.</summary>
        public void OnPlayerSpawned(String strSoldierName, Inventory spawnedInventory)
        {
            if (mEnabled && !mTsReconnecting)
                addToActionQueue(Commands.PlayerSpawned, strSoldierName);
        }

        /// <summary>Is called when a player joins the server.</summary>
        public void OnPlayerJoin(String strSoldierName)
        {
            if (mEnabled && !mTsReconnecting)
                addToActionQueue(Commands.PlayerJoined, strSoldierName);
        }
        /// <summary>Is called when a player leaves the server.</summary>
        public void OnPlayerLeft(String strSoldierName)
        {
            if (mEnabled && !mTsReconnecting)
                addToActionQueue(Commands.PlayerLeft, strSoldierName);
        }
        /// <summary>Is called when a player changes teams.</summary>
        public void OnPlayerTeamChange(String strSoldierName, Int32 iTeamID, Int32 iSquadID)
        {
            if (mEnabled && !mTsReconnecting)
                addToActionQueue(Commands.PlayerSwappedTeamsOrSquads, strSoldierName, iTeamID, iSquadID);
        }
        /// <summary>Is called when a player changes squads.</summary>
        public void OnPlayerSquadChange(String strSoldierName, Int32 iTeamID, Int32 iSquadID)
        {
            if (mEnabled && !mTsReconnecting)
                addToActionQueue(Commands.PlayerSwappedTeamsOrSquads, strSoldierName, iTeamID, iSquadID);
        }
        /// <summary>Is called when a player list is received.</summary>
        public void OnListPlayers(List<CPlayerInfo> lstPlayers, CPlayerSubset cpsSubset)
        {
            if (mEnabled && !mTsReconnecting)
                if (cpsSubset.Subset == CPlayerSubset.PlayerSubsetType.All)
                    addToActionQueue(Commands.UpdateGmClientInfo, lstPlayers);
        }
        /// <summary>Is called when a single player's punkbuster info is received.</summary>
        public void OnPunkbusterPlayerInfo(CPunkbusterInfo cpbiPlayer)
        {
            if (mEnabled && !mTsReconnecting)
                addToActionQueue(Commands.UpdatePbClientInfo, cpbiPlayer);
        }
        /// <summary>Is called when the round is started.</summary>
        public void OnLevelLoaded(String mapFileName, String Gamemode, Int32 roundsPlayed, Int32 roundsTotal)
        {
            mBetweenRounds = false;
            if (mEnabled && !mTsReconnecting)
                addToActionQueue(Commands.CheckAllClientsForSwapping);
        }
        /// <summary>Is called when the round is ended.</summary>
        public void OnRoundOver(Int32 iWinningTeamID)
        {
            mBetweenRounds = true;
            if (mEnabled && !mTsReconnecting)
            {
                addToActionQueue(Commands.ResetAllUsersSyncFlags);
                addToActionQueue(Commands.CheckAllClientsForSwapping);
            }
                
        }
        /// <summary>Used to handle the in-game control commands. </summary>
        public void OnGlobalChat(string speaker, string message)
        {
            
            if (mEnabled && !mTsReconnecting)
            {
                //Figure out which command to send. 
                switch (message)
                {
                    case "!tssquads":
                        addToActionQueue(Commands.DisplayTSSquadList, speaker);
                        break;
                    case "!tslobby":
                        addToActionQueue(Commands.SetSyncToStaging, speaker);
                        break;
                    case "!tsteam":
                        addToActionQueue(Commands.SetSyncToTeam, speaker);
                        break;
                    case "/!tsnosync":
                        addToActionQueue(Commands.SetNoSync, speaker);
                        break;
                    case "!tssync":
                        addToActionQueue(Commands.ResetUserSyncFlags, speaker);
                        break;
                }
            }
        }
        /// <summary>Used to handle the in-game control commands. </summary>
        public void OnTeamChat(string speaker, string message, int teamId)
        {
            //Uses the same logic as OnGlobalChat, so just call that.
            OnGlobalChat(speaker, message);
        }
        /// <summary>Used to handle the in-game control commands. </summary>
        public void OnSquadChat(string speaker, string message, int teamId, int squadId)
        {
            //Uses the same logic as OnGlobalChat, so just call that.
            OnGlobalChat(speaker, message);
        }



        /// <summary>Is called whenever data is sent over the network.</summary>
        /// <param name="data">The raw data that was sent.</param>
        public void DataSent(String data)
        {
            debugWrite(dbgNetwork, "[DataSent] {0}", data);
        }
        /// <summary>Is called whenever data is received over the network.</summary>
        /// <param name="data">The raw data that was received.</param>
        public void DataReceived(String data)
        {
            foreach (String line in data.Replace("\r", "").Split('\n'))
                debugWrite(dbgNetwork, "[DataReceived] {0}", line);
        }
        /// <summary>Is called by a timer.  Assumes top of queue is timer that called method.</summary>
        /// <param name="Name">The name of the player that joined.</param>
        public void MessageOnJoinCallback(Object Name)
        {
            mMessageTimers.Dequeue().Dispose();
            addToActionQueue(Commands.CheckClientForMessagingAfterJoining, Name);
        }



        /// <summary>Loops until Procon is shutdown.</summary>
        public void EntryMain()
        {
            // Register for events regarding logging for protocol level stuffs.
            mTsConnection.DataSent += DataSent;
            mTsConnection.DataReceived += DataReceived;

            // Loop Indefinately
            while (true)
            {
                // Grab an action from the queue.
                mActionSemaphore.WaitOne();
                mActionMutex.WaitOne();
                mCurrentAction = mActions.Dequeue();
                mActionMutex.ReleaseMutex();

                // Remove invalid commands.
                if (!mEnabled && mCurrentAction.Command != Commands.PluginEnabled && mCurrentAction.Command != Commands.PluginDisabled)
                    continue;

                // Perform the specific command.
                try
                {
                    switch (mCurrentAction.Command)
                    {
                        case Commands.PluginEnabled:
                            debugWrite(dbgEvents, "[Event] Processing Plugin Enabled Event.");
                            performOpenConnection();
                            break;
                        case Commands.PluginDisabled:
                            debugWrite(dbgEvents, "[Event] Processing Plugin Disabled Event.");
                            performCloseConnection();
                            break;

                        case Commands.UpdateTsClientInfo:
                            debugWrite(dbgEvents, "[Event] Processing Update Teamspeak Information Event.");
                            updateTsInfo();
                            break;
                        case Commands.UpdateGmClientInfo:
                            debugWrite(dbgEvents, "[Event] Processing Update Player Information Event.");
                            updateGmInfo((List<CPlayerInfo>)mCurrentAction.Argument);
                            break;
                        case Commands.UpdatePbClientInfo:
                            debugWrite(dbgEvents, "[Event] Processing Update Punkbuster Information Event.");
                            updatePbInfo((CPunkbusterInfo)mCurrentAction.Argument);
                            break;
                        case Commands.UpdateMsClientInfo:
                            debugWrite(dbgEvents, "[Event] Processing Update Master Information Event.");
                            updateMasterInfo();
                            break;

                        case Commands.CheckAllClientsForSwapping:
                            debugWrite(dbgEvents, "[Event] Processing Swap ALL Clients Event.");
                            foreach (MasterClient mstClient in getPlayersOnBothServers())
                                addToActionQueue(Commands.CheckClientForSwapping, mstClient);
                            break;
                        case Commands.CheckAllClientsForRemoval:
                            debugWrite(dbgEvents, "[Event] Processing Remove ALL Clients Event.");
                            foreach (MasterClient mstClient in getPlayersOnTsOnly())
                                addToActionQueue(Commands.CheckClientForRemoval, mstClient);
                            break;
                        case Commands.CheckAllClientsForMessaging:
                            debugWrite(dbgEvents, "[Event] Processing Messaging To ALL Clients Event.");
                            foreach (MasterClient mstClient in getPlayersOnBcOnly())
                                addToActionQueue(Commands.CheckClientForMessaging, mstClient);
                            break;
                        case Commands.CheckClientForSwapping:
                            debugWrite(dbgEvents, "[Event] Processing Swap Client Event.");
                            checkClientForSwap((MasterClient)mCurrentAction.Argument);
                            break;
                        case Commands.CheckClientForRemoval:
                            debugWrite(dbgEvents, "[Event] Processing Remove Client Event.");
                            checkClientForRemove((MasterClient)mCurrentAction.Argument);
                            break;
                        case Commands.CheckClientForMessaging:
                            debugWrite(dbgEvents, "[Event] Processing Messaging To Client Event.");
                            checkClientForMessage((MasterClient)mCurrentAction.Argument);
                            break;
                        case Commands.CheckClientForMessagingAfterJoining:
                            debugWrite(dbgEvents, "[Event] Processing Messaging On Join Event.");
                            String Name = (String)mCurrentAction.Argument;
                            foreach (MasterClient mstClient in getPlayersOnBcOnly())
                                if (mstClient.GmClient.Name == Name)
                                {
                                    checkClientForMessage(mstClient);
                                    break;
                                }
                            break;

                        case Commands.PlayerJoined:
                            debugWrite(dbgEvents, "[Event] Processing Player Joined Event.");
                            playerJoined((String)mCurrentAction.Argument);
                            break;
                        case Commands.PlayerLeft:
                            debugWrite(dbgEvents, "[Event] Processing Player Left Event.");
                            playerLeft((String)mCurrentAction.Argument);
                            break;
                        case Commands.PlayerSpawned:
                            debugWrite(dbgEvents, "[Event] Processing Player Spawn Event.");
                            playerSpawned((String)mCurrentAction.Argument);
                            break;
                        case Commands.PlayerSwappedTeamsOrSquads:
                            debugWrite(dbgEvents, "[Event] Processing Swapped Teams or Squads Event.");
                            playerSwappedTeamsOrSquads((String)mCurrentAction.Argument, (Int32)mCurrentAction.Argument, (Int32)mCurrentAction.Argument);
                            break;
                        case Commands.SetSyncToStaging:
                            if(mEnableTSStaging)
                            {
                                debugWrite(dbgEvents, "[Event] Processing Sync to Staging event for Player.");
                                SetSyncToStagingFlagForPlayer((string)mCurrentAction.Argument);
                            }
                            break;
                        case Commands.SetSyncToTeam:
                            if(mEnableTSTeam)
                            {
                                debugWrite(dbgEvents, "[Event] Processing Set Sync to Team event for Player.");
                                SetSyncToTeamFlagForPlayer((string)mCurrentAction.Argument);
                            }
                            break;
                        case Commands.SetNoSync:
                            if(mEnableTSNoSync)
                            {
                                debugWrite(dbgEvents, "[Event] Processing Set No Sync event for Player.");
                                SetNoSyncFlagForPlayer((string)mCurrentAction.Argument);
                            }
                            break; 
                        case Commands.ResetUserSyncFlags:
                            debugWrite(dbgEvents, "[Event] Processing Sync Flag Reset for Player.");
                            ResetSyncFlagsForPlayer((string) mCurrentAction.Argument);
                            break;
                        case Commands.ResetAllUsersSyncFlags:
                            debugWrite(dbgEvents, "[Event] Resetting all player sync flags.");
                            ResetAllUserSyncFlags();
                            break;
                        case Commands.DisplayTSSquadList:
                            if(mEnableTSSquadList)
                            {
                                debugWrite(dbgEvents, "[Event] Processing DisplayTSSquadList event.");
                                DisplayTsSquadList((string)mCurrentAction.Argument);
                            }
                            
                            break;
                    }
                } catch (Exception e) {
                    consoleWrite("^8A fatal error occurred during processing a command!");
                    consoleWrite("^8^bMessage:^n^0 " + e.Message);
                    consoleWrite("^8^bStack Trace:^n^0 " + e.StackTrace);
                    setPluginState(false);
                }

                // Record the event that was just processed.
                mPreviousAction = mCurrentAction;
            }
        }
        /// <summary>Loops intermittently until Procon is shutdown: Checks clients for messaging.</summary>
        public void EntryMessaging()
        {
            // Loop Indefinately
            while (true)
            {
                if (mEnabled && !mTsReconnecting && msgEnabled) addToActionQueue(Commands.CheckAllClientsForMessaging);
                Thread.Sleep(msgInterval);
            }
        }
        /// <summary>Loops intermittently until Procon is shutdown: Updates Teamspeak info, Checks clients for removal.</summary>
        public void EntrySynchronization()
        {
            // Loop Indefinately
            while (true)
            {
                if (mEnabled && !mTsReconnecting)
                {
                    addToActionQueue(Commands.UpdateTsClientInfo);
                    addToActionQueue(Commands.UpdateMsClientInfo);
                    addToActionQueue(Commands.CheckAllClientsForSwapping);
                    if (synRemoveClients) addToActionQueue(Commands.CheckAllClientsForRemoval);
                }
                Thread.Sleep(synUpdateInterval);
            }
        }



        /// <summary>Opens a connection to the Teamspeak Server and obtains all necessary server information.</summary>
        public void performOpenConnection()
        {
            // -- Wait ( 10 seconds ) for data to be initialized.
            for (int secondsSlept = 0; secondsSlept < 10 && ts3ServerIp == "Teamspeak Ip"; secondsSlept++)
                Thread.Sleep(1000);

            // -- Required: Connection, Login, Use, Staging Channel.
            consoleWrite("[Connection] Establishing a connection to a Teamspeak 3 Server.");
            mTsResponse = mTsConnection.open(ts3ServerIp, ts3QueryPort);
            if (!performResponseHandling(Queries.OpenConnectionEstablish)) return;
            consoleWrite("[Connection] ^2Established a connection to {0}:{1}.", ts3ServerIp, ts3QueryPort);

            consoleWrite("[Connection] Attempting to login as a Server Query Client.");
            sendTeamspeakQuery(TeamspeakQuery.buildLoginQuery(ts3QueryUsername, ts3QueryPassword));
            if (!performResponseHandling(Queries.OpenConnectionLogin)) return;
            consoleWrite("[Connection] ^2Logged in as {0}.", ts3QueryUsername);

            consoleWrite("[Connection] Attempting to select the correct virtual server.");
            sendTeamspeakQuery(TeamspeakQuery.buildUsePortQuery(ts3ServerPort));
            if (!performResponseHandling(Queries.OpenConnectionUse)) return;
            consoleWrite("[Connection] ^2Selected the virtual server using port {0}.", ts3ServerPort);

            consoleWrite("[Connection] Attempting to find the staging channel.");
            sendTeamspeakQuery(TeamspeakQuery.buildChannelFindQuery(ts3StgChannelName));
            if (!performResponseHandling(Queries.OpenConnectionStaging)) return;
            mStagingChannel.setBasicData(mTsResponse.Sections[0].Groups[0]);
            consoleWrite("[Connection] ^2Found the channel named {0}.", mStagingChannel.tsName);

            if (ts3EnableDropoff)
            {
                consoleWrite("[Connection] Attempting to find the staging channel.");
                sendTeamspeakQuery(TeamspeakQuery.buildChannelFindQuery(ts3DropoffChannelName));
                if (!performResponseHandling(Queries.OpenConnectionStaging)) return;
                mDropoffChannel.setBasicData(mTsResponse.Sections[0].Groups[0]);
                consoleWrite("[Connection] ^2Found the channel named {0}.", mDropoffChannel.tsName);
            }

            // -- Optional: Change Nickname.
            consoleWrite("[Connection] Attempting to alter the Server Query Client's name.");
            sendTeamspeakQuery(TeamspeakQuery.buildChangeNicknameQuery(ts3QueryNickname));
            if (!performResponseHandling(Queries.OpenConnectionNickname)) return;
            if (mTsResponse.Id != "513") consoleWrite("[Connection] ^2Changed the Server Query Client's name to {0}.", ts3QueryNickname);
            mTsResponse = new TeamspeakResponse("error id=0 msg=ok");

            // -- Trivial: Find Existing Channels.
            consoleWrite("[Connection] Attempting to find existing pickup, team, and squad channels.");
            sendTeamspeakQuery(TeamspeakQuery.buildChannelListQuery());
            List<TeamspeakChannel> tsChannels = new List<TeamspeakChannel>();
            foreach (TeamspeakResponseSection tsResponseSection in mTsResponse.Sections)
                foreach (TeamspeakResponseGroup tsResponseGroup in tsResponseSection.Groups)
                    tsChannels.Add(new TeamspeakChannel(tsResponseGroup));
            foreach (TeamspeakChannel tsChannel in tsChannels)
                foreach (String tsName in ts3PckChannelNames)
                    if (tsChannel.tsName == tsName)
                    {
                        mPickupChannels.Add(tsChannel);
                        consoleWrite("[Connection] ^2Found ^bPickup^n Channel: {0} ({1}).", tsChannel.tsName, tsChannel.tsId);
                        break;
                    }
            foreach (TeamspeakChannel tsChannel in tsChannels)
                if (tsChannel.medPId == 0 || tsChannel.medPId == mStagingChannel.tsId)
                    for (int i = 0; i < chnTeamNames.Length; i++)
                        if (!mTeamChannels.ContainsKey(i + 1) && tsChannel.tsName == chnTeamNames[i])
                        {
                            mTeamChannels.Add(i + 1, tsChannel);
                            mSquadChannels.Add(i + 1, new Dictionary<Int32, TeamspeakChannel>());
                            consoleWrite("[Connection] ^2Found ^bTeam^n Channel: {0} ({1}).", tsChannel.tsName, tsChannel.tsId);
                            break;
                        }
            foreach (TeamspeakChannel tsChannel in tsChannels)
                foreach (Int32 teamId in mTeamChannels.Keys)
                    if (tsChannel.medPId == mTeamChannels[teamId].tsId)
                        for (int i = 0; i < chnSquadNames.Length; i++)
                            if (!mSquadChannels[teamId].ContainsKey(i + 1) && tsChannel.tsName == chnSquadNames[i])
                            {
                                mSquadChannels[teamId].Add(i + 1, tsChannel);
                                consoleWrite("[Connection] ^2Found ^bSquad^n Channel: {0} ({1}).", tsChannel.tsName, tsChannel.tsId);
                                break;
                            }

            // -- Done.
            consoleWrite("[Connection] Teamspeak 3 Sync started.");
            mEnabled = true;
        }
        /// <summary>Closes a connection to the Teamspeak Server and clears all the data stored within the plugin.</summary>
        public void performCloseConnection()
        {
            consoleWrite("[Closing] Shutting down Teamspeak 3 Sync.");
            mTsConnection.close();

            consoleWrite("[Closing] Cleaning up resources.");
            mClientAllInfo.Clear();
            mClientTsInfo.Clear();
            mClientGmInfo.Clear();
            mClientPbInfo.Clear();
            mTeamChannels.Clear();
            mSquadChannels.Clear();
            mPickupChannels.Clear();
            mTsResponse = new TeamspeakResponse("error id=0 msg=ok");
            mCurrentAction = null;
            mPreviousAction = null;

            consoleWrite("[Closing] Teamspeak 3 Sync stopped.");
            mEnabled = false;
        }
        /// <summary>Handles errors reported via the teamspeak responses.</summary>
        public Boolean performResponseHandling(Queries queryCode)
        {
            // Exit if we're fine.
            if (mTsResponse.Id == "0")
                return true;

            // Handle "Always Fine" or "Always Fatal" issues.
            switch (mTsResponse.Id)
            {
                case "-1": // Socket was open and we tried to re-establish a connection.
                case "-5": // Socket was closed and we tried to send a query.
                case "-6": // The query we tried to send was null.
                    consoleWrite("[Error] ^3Minor Error:");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return true;

                case "-2": // Invalid IP Address.
                case "-3": // Invalid Port.
                case "-4": // Error occurred when trying to establish a connection.
                    consoleWrite("[Error] ^8Fatal Error:");
                    consoleWrite("[Error] ^8An error occurred during establishing a connection to the Teamspeak 3 Server.");
                    consoleWrite("[Error] ^8Make sure your ^b\"Server Ip\"^n and ^b\"Query Port\"^n are correct.");
                    consoleWrite("[Error] ^8{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    if (!mTsReconnecting && errReconnectOnError && performReconnect()) break;
                    setPluginState(false);
                    return false;

                case "-7": // Error occurred during sending the query.
                case "-8": // Error occurred during receiving the response.
                    consoleWrite("[Error] ^8Fatal Error:");
                    consoleWrite("[Error] ^8An error occurred during sending and receiving data to the Teamspeak 3 Server.");
                    consoleWrite("[Error] ^8{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    if (!mTsReconnecting && errReconnectOnError && performReconnect()) break;
                    setPluginState(false);
                    return false;

                case "3329": // You are temp banned from the server for flooding.
                case "3331": // You are temp banned from the server for 'x' seconds.
                    consoleWrite("[Error] ^8Fatal Error:");
                    consoleWrite("[Error] ^8You were temporarily banned from the Teamspeak 3 Server for flooding.");
                    consoleWrite("[Error] ^8Make sure your ^bProcon's Ip^n is in your ^bTeamspeak 3 Server's Whitelist^n.");
                    consoleWrite("[Error] ^8{0}: {1} ({2})", mTsResponse.Id, mTsResponse.Message, mTsResponse.ExtraMessage);
                    setPluginState(false);
                    return false;
            }

            // Handle "Program Position Specific" issues.
            switch (queryCode)
            {
                // -- Perform Open Connection ---------------------------------
                case Queries.OpenConnectionEstablish:
                    consoleWrite("[Error] ^8Fatal Error:");
                    consoleWrite("[Error] ^8An error occurred during establishing a connection to the Teamspeak 3 Server.");
                    consoleWrite("[Error] ^8Make sure your ^b\"Server Ip\"^n and ^b\"Query Port\"^n are correct.");
                    consoleWrite("[Error] ^8{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    setPluginState(false);
                    return false;
                case Queries.OpenConnectionLogin:
                    consoleWrite("[Error] ^8Fatal Error:");
                    consoleWrite("[Error] ^8An error occurred during logging into the Teamspeak 3 Server.");
                    consoleWrite("[Error] ^8Make sure your ^b\"Query Username\"^n and ^b\"Query Password\"^n are correct.");
                    consoleWrite("[Error] ^8{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    setPluginState(false);
                    return false;
                case Queries.OpenConnectionUse:
                    consoleWrite("[Error] ^8Fatal Error:");
                    consoleWrite("[Error] ^8An error occurred during finding the virtual server.");
                    consoleWrite("[Error] ^8Make sure your ^b\"Server Port\"^n is correct.");
                    consoleWrite("[Error] ^8{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    setPluginState(false);
                    return false;
                case Queries.OpenConnectionStaging:
                    consoleWrite("[Error] ^8Fatal Error:");
                    consoleWrite("[Error] ^8An error occurred during finding the staging channel.");
                    consoleWrite("[Error] ^8Make sure your ^b\"Staging Channel Name\"^n is correct and that the channel exists in the Teamspeak 3 Server.");
                    consoleWrite("[Error] ^8{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    setPluginState(false);
                    return false;
                case Queries.OpenConnectionNickname:
                    consoleWrite("[Error] ^3Minor Error:");
                    consoleWrite("[Error] ^3An error occurred during changing the server query nickname.");
                    consoleWrite("[Error] ^3Make sure your ^b\"Query Nickname\"^n is not already in use.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return true;


                // -- Update Teamspeak Information ----------------------------
                case Queries.TsInfoClientList:
                    consoleWrite("[Error] ^3Minor Error - Update Teamspeak Information:");
                    consoleWrite("[Error] ^3An error occurred during obtaining the Teamspeak Client List.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.TsInfoChannelList:
                    consoleWrite("[Error] ^3Minor Error - Update Teamspeak Information:");
                    consoleWrite("[Error] ^3An error occurred during obtaining the Teamspeak Channel List.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.TsInfoClientInfo:
                    consoleWrite("[Error] ^3Minor Error - Update Teamspeak Information:");
                    consoleWrite("[Error] ^3An error occurred during obtaining an Advanced Client Information.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return true;

                // -- Check Client for Swapping/Removing ----------------------
                case Queries.CheckSwapStaging:
                    consoleWrite("[Error] ^3Minor Error - Check Client for Swapping/Removing:");
                    consoleWrite("[Error] ^3An error occurred during moving a client to the staging channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.CheckSwapTeam:
                    consoleWrite("[Error] ^3Minor Error - Check Client for Swapping/Removing:");
                    consoleWrite("[Error] ^3An error occurred during moving a client to a team channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.CheckSwapSquad:
                    consoleWrite("[Error] ^3Minor Error - Check Client for Swapping/Removing:");
                    consoleWrite("[Error] ^3An error occurred during moving a client to a squad channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.CheckRemove:
                    consoleWrite("[Error] ^3Minor Error - Check Client for Swapping/Removing:");
                    consoleWrite("[Error] ^3An error occurred during removing a client to the staging channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;

                // -- Find or Create Channels ---------------------------------
                case Queries.FindTeamChannelList:
                    consoleWrite("[Error] ^3Minor Error - Find or Create Channels:");
                    consoleWrite("[Error] ^3An error occurred during obtaining the Teamspeak Channel List while looking for a team channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.FindSquadChannelList:
                    consoleWrite("[Error] ^3Minor Error - Find or Create Channels:");
                    consoleWrite("[Error] ^3An error occurred during obtaining the Teamspeak Channel List while looking for a squad channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.CreateTeamChannelQuery:
                    consoleWrite("[Error] ^3Minor Error - Find or Create Channels:");
                    consoleWrite("[Error] ^3An error occurred during creating a team channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.CreateTeamChannelInfo:
                    consoleWrite("[Error] ^3Minor Error - Find or Create Channels:");
                    consoleWrite("[Error] ^3An error occurred during requesting advanced information about the new team channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.CreateSquadChannelQuery:
                    consoleWrite("[Error] ^3Minor Error - Find or Create Channels:");
                    consoleWrite("[Error] ^3An error occurred during creating a squad channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.CreateSquadChannelInfo:
                    consoleWrite("[Error] ^3Minor Error - Find or Create Channels:");
                    consoleWrite("[Error] ^3An error occurred during requesting advanced information about the new squad channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;

                // -- Remove Channels -----------------------------------------
                case Queries.RemoveChannelsList:
                    consoleWrite("[Error] ^3Minor Error - Remove Channels:");
                    consoleWrite("[Error] ^3An error occurred during obtaining the Teamspeak Channel List.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return false;
                case Queries.RemoveChannelsTeamQuery:
                    consoleWrite("[Error] ^3Minor Error - Remove Channels:");
                    consoleWrite("[Error] ^3An error occurred during removing a team channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return true;
                case Queries.RemoveChannelsSquadQuery:
                    consoleWrite("[Error] ^3Minor Error - Remove Channels:");
                    consoleWrite("[Error] ^3An error occurred during removing a squad channel.");
                    consoleWrite("[Error] ^3{0}: {1}", mTsResponse.Id, mTsResponse.Message);
                    return true;
            }

            // Don't know what went wrong?
            return true;
        }
        /// <summary>Attempts to reconnect to the teamspeak server.</summary>
        public Boolean performReconnect()
        {
            consoleWrite("[Reconnect] Attempting to establish a new connection to the Teamspeak 3 Server.");
            mTsReconnecting = true;
            for (int attempt = 1; attempt <= errReconnectOnErrorAttempts; attempt++)
            {
                // -- Sleep if this isn't our first attempt.
                if (attempt != 1)
                    mTsReconnEvent.WaitOne(errReconnectOnErrorInterval);

                mActionMutex.WaitOne();
                ActionEvent tAction = (mActions.Count == 0) ? null : mActions.Peek();
                mActionMutex.ReleaseMutex();

                if (tAction == null || tAction.Command != Commands.PluginDisabled)
                {
                    // -- Attempt to establish a new connection.
                    mTsConnection.close();
                    performOpenConnection();
                    if (mTsResponse.Id == "0") { mTsReconnecting = false; return true; }
                    // -- Notify we've failed.
                    consoleWrite("[Reconnect] Failed {0}.",
                        (attempt < errReconnectOnErrorAttempts) ?
                            ("attempt " + attempt + " out of " + errReconnectOnErrorAttempts) :
                            ("the last attempt."));
                }
                else attempt = errReconnectOnErrorAttempts + 1;
            }
            mTsReconnecting = false;
            return false;
        }



        /// <summary>Obtain the current Teamspeak Client list for the Bad Company channels.</summary>
        public void updateTsInfo()
        {
            // Build a list of clients in the server.
            List<TeamspeakClient> clientInfo = new List<TeamspeakClient>();
            // Request all the clients basic information.  Bail out if the query was bad.
            sendTeamspeakQuery(TeamspeakQuery.buildClientListQuery());
            if (!performResponseHandling(Queries.TsInfoClientList))
                return;
            // Build list of clients.
            foreach (TeamspeakResponseSection sec in mTsResponse.Sections)
                foreach (TeamspeakResponseGroup grp in sec.Groups)
                    clientInfo.Add(new TeamspeakClient(grp));


            // Build a list of channels in the server.
            List<TeamspeakChannel> channelInfo = new List<TeamspeakChannel>();
            // Request all the channels be sent back.  Bail out if the query was bad.
            sendTeamspeakQuery(TeamspeakQuery.buildChannelListQuery());
            if (!performResponseHandling(Queries.TsInfoChannelList))
                return;
            // Build a list of channels.
            foreach (TeamspeakResponseSection sec in mTsResponse.Sections)
                foreach (TeamspeakResponseGroup grp in sec.Groups)
                    channelInfo.Add(new TeamspeakChannel(grp));

            // Clean list of users who aren't in a known channel
            for (int i = 0; i < clientInfo.Count; i++)
            {
                Boolean inChannel = false;
                // Check to see if user is in staging channel.
                if (clientInfo[i].medChannelId == mStagingChannel.tsId)
                    inChannel = true;
                // Check to see if the user is in a pickup channel.
                foreach (TeamspeakChannel pickupChannel in mPickupChannels)
                    if (clientInfo[i].medChannelId == pickupChannel.tsId)
                        inChannel = true;
                // Check to see if the user is in a team channel.
                foreach (TeamspeakChannel teamChannel in mTeamChannels.Values)
                    if (clientInfo[i].medChannelId == teamChannel.tsId)
                        inChannel = true;
                // Check to see if the user is in a squad channel.
                foreach (Dictionary<Int32, TeamspeakChannel> teamChannels in mSquadChannels.Values)
                    foreach (TeamspeakChannel squadChannel in teamChannels.Values)
                        if (clientInfo[i].medChannelId == squadChannel.tsId)
                            inChannel = true;

                // Remove user if they're not in any of the channels.
                if (!inChannel)
                    clientInfo.RemoveAt(i--);
            }


            // Build an advanced information list of clients in the proper channels.
            for (int i = 0; i < clientInfo.Count; i++)
            {
                // Request the advanced information for a client.
                sendTeamspeakQuery(TeamspeakQuery.buildClientInfoQuery(clientInfo[i].tsId.Value));

                // Check if response had connection error.
                if (!performResponseHandling(Queries.TsInfoClientInfo))
                    return;
                // Check if response had query error.
                else if (mTsResponse.Id != "0")
                    continue;
                // Sanity check on the ts3Response.
                else if (!mTsResponse.HasSections || !mTsResponse.Sections[0].HasGroups)
                    continue;

                // Update client.
                clientInfo[i].setAdvancedData(mTsResponse.Sections[0].Groups[0]);
            }


            // Clean up clients who had faulty responses.
            for (int i = 0; i < clientInfo.Count; i++)
                if (clientInfo[i].advIpAddress == null)
                    clientInfo.RemoveAt(i--);
            // Replace global list with new list.
            mClientTsInfo = clientInfo;


            // Debug player information print.
            debugWrite(dbgClients, "[Clients] Result of Teamspeak Client Update:");
            foreach (TeamspeakClient tsClient in mClientTsInfo)
                debugWrite(dbgClients, "- TS Client [Ip: {0}, Channel: {1}, Name: {2}]", tsClient.advIpAddress, tsClient.medChannelId, tsClient.tsName);
        }
        /// <summary>Obtain the current Game Client list for the players on the game.</summary>
        /// <param name="generalInfo">The current list of players on the game.</param>
        public void updateGmInfo(List<CPlayerInfo> generalInfo)
        {
            // Build a list of general information clients in the game.
            List<GameClient> clientInfo = new List<GameClient>();
            // Build a list of general information clients.
            foreach (CPlayerInfo genInfo in generalInfo)
                clientInfo.Add(new GameClient(genInfo));


            // Build a list of all-inclusive information clients in the game.
            Dictionary<String, CPunkbusterInfo> punkbusterInfo = new Dictionary<String, CPunkbusterInfo>();
            // Check if we have punkbuster information for each Game Client.
            foreach (GameClient gmClient in clientInfo)
                if (mClientPbInfo.ContainsKey(gmClient.Name))
                {
                    gmClient.PunkbusterInfo = mClientPbInfo[gmClient.Name];
                    punkbusterInfo.Add(gmClient.Name, mClientPbInfo[gmClient.Name]);
                }


            // Replace the global lists with the new lists.
            mClientPbInfo = punkbusterInfo;
            mClientGmInfo = clientInfo;


            // Debug player information print.
            debugWrite(dbgClients, "[Clients] Result of Game Client Update:");
            foreach (GameClient gmClient in mClientGmInfo)
                debugWrite(dbgClients, "- GM Client [Ip: {0}, Team: {1}, Name: {2}]", (gmClient.HasPbInfo ? gmClient.IP : "Null_IP"), gmClient.TeamId, gmClient.Name);
        }
        /// <summary>Updates the punkbuster information with new information.</summary>
        /// <param name="punkInfo">The punkbuster information to add.</param>
        public void updatePbInfo(CPunkbusterInfo punkInfo)
        {
            // Remove the port off the IP of the punkbuster info.
            punkInfo = new CPunkbusterInfo(
                punkInfo.SlotID,
                punkInfo.SoldierName,
                punkInfo.GUID,
                punkInfo.Ip.Remove((punkInfo.Ip.Contains(":") ? punkInfo.Ip.IndexOf(':') : 0)),
                punkInfo.PlayerCountry, punkInfo.PlayerCountryCode);

            // If it's in the list already and it has ip information, just replace it.  Otherwise, add it to the list.
            if (mClientPbInfo.ContainsKey(punkInfo.SoldierName))
            {
                if (punkInfo.Ip != "")
                    mClientPbInfo[punkInfo.SoldierName] = punkInfo;
            }
            else
                mClientPbInfo.Add(punkInfo.SoldierName, punkInfo);

            // Debug player information print.
            debugWrite(dbgClients, "[Clients] Result of Punkbuster Client Update:");
            debugWrite(dbgClients, "- PB Client [Ip: {0}, Name: {1}]", punkInfo.Ip, punkInfo.SoldierName);
        }
        /// <summary>Builds a new master list using the information obtained by other methods.</summary>
        public void updateMasterInfo()
        {
            // [0] Used to hold the current client info.
            // [1] Used to hold the Game Client Master Clients who could not be matched.
            // [2] Used to hold the clients whose IP matched the Game Client's IP
            List<MasterClient> clientInfo = new List<MasterClient>();
            List<MasterClient> newClientInfo = new List<MasterClient>();
            List<MasterClient> matchOnIp = new List<MasterClient>();


            // Create Master Clients out of teamspeak clients.
            foreach (TeamspeakClient tsClient in mClientTsInfo)
                clientInfo.Add(new MasterClient(tsClient));


            // First, determine if the game client has an IP.
            foreach (GameClient gmClient in mClientGmInfo)
                #region Match Game Clients (with IPs) to Master Clients

                if (gmClient.HasPbInfo)
                {
                    // Second, build a list of clients from the master list whom IP matches this gm client.
                    matchOnIp.Clear();
                    foreach (MasterClient mstClient in clientInfo)
                        if (gmClient.IP == mstClient.TsClient.advIpAddress)
                            matchOnIp.Add(mstClient);

                    // Multiple matches require sorting.
                    if (matchOnIp.Count > 1)
                    {
                        Int32 matchIndex = 0;
                        Double biggestPercent = 0;
                        Double currentPercent = 0;
                        for (int i = 0; i < matchOnIp.Count; i++)
                        {
                            currentPercent = MasterClient.calcPercentMatch(gmClient.Name.ToLower(), matchOnIp[i].TsClient.tsName.ToLower());
                            if (currentPercent > biggestPercent)
                            {
                                matchIndex = i;
                                biggestPercent = currentPercent;
                            }
                        }
                        matchOnIp[matchIndex].GmClient = gmClient;
                    }
                    // Single matches are simply one-to-one.
                    else if (matchOnIp.Count == 1)
                    {
                        matchOnIp[0].GmClient = gmClient;
                    }
                    // No matches are players not in teamspeak.
                    else
                    {
                        newClientInfo.Add(new MasterClient(gmClient));
                    }
                }

                #endregion
                #region Match Game Clients (without IPs) to Master Clients

                else
                {
                    // Second, because the game client does not have an IP, try to match the name.
                    Int32 matchIndex = 0;
                    Double biggestPercent = 0;
                    Double currentPercent = 0;
                    for (int i = 0; i < clientInfo.Count; i++)
                        if (!clientInfo[i].HasGmClient)
                        {
                            currentPercent = MasterClient.calcPercentMatch(gmClient.Name.ToLower(), clientInfo[i].TsClient.tsName.ToLower());
                            if (currentPercent > biggestPercent)
                            {
                                matchIndex = i;
                                biggestPercent = currentPercent;
                            }
                        }

                    // Matches above the threshold are put together.
                    if (biggestPercent >= synMatchingThreshold)
                        clientInfo[matchIndex].GmClient = gmClient;
                    // Matches below the threshold are not in teamspeak.
                    else
                        newClientInfo.Add(new MasterClient(gmClient));
                }

                #endregion


            // Consolidate master client lists and update global list.
            clientInfo.AddRange(newClientInfo);

            // Update clientInfo to have the sync flags of all previously connected users.  
            foreach(MasterClient mstClient in mClientAllInfo)
            {
                foreach(MasterClient innerClient in clientInfo)
                {
                    if (mstClient.HasGmClient && innerClient.HasGmClient)
                    {
                        if (mstClient.GmClient.Name == innerClient.GmClient.Name)
                        {
                            innerClient.IsSyncToTeam = mstClient.IsSyncToTeam;
                            innerClient.IsSyncToStaging = mstClient.IsSyncToStaging;
                            innerClient.IsNoSync = mstClient.IsNoSync;
                            break;
                        }
                    }
                }
            }

            mClientAllInfo = clientInfo;


            // Debug player information print.
            debugWrite(dbgClients, "[Clients] Result of Master Client Update:");
            foreach (MasterClient mstClient in mClientAllInfo)
                debugWrite(dbgClients, "- TS Client [Ip: {0}, Channel: {1}, Name: {2}] / GM Client [Ip: {3}, Team: {4}, Name: {5}] / Nosync: {6}, SyncTeam: {7}, SyncStaging: {8}",
                                (mstClient.HasTsClient) ? mstClient.TsClient.advIpAddress : "Null_IP",
                                (mstClient.HasTsClient) ? mstClient.TsClient.medChannelId.Value.ToString() : "Null_Channel",
                                (mstClient.HasTsClient) ? mstClient.TsClient.tsName : "Null_Name",
                                (mstClient.HasGmClient) ? ((mstClient.GmClient.HasPbInfo) ? mstClient.GmClient.IP : "Null_IP") : "Null_IP",
                                (mstClient.HasGmClient) ? mstClient.GmClient.TeamId.ToString() : "Null_Team",
                                (mstClient.HasGmClient) ? mstClient.GmClient.Name                                 : "Null_Name",
                                mstClient.IsNoSync, mstClient.IsSyncToTeam, mstClient.IsSyncToStaging
                            );
        }



        /// <summary>Moves a client into the correct channel, if they're in the wrong channel.</summary>
        /// <param name="client">The client to check.</param>
        public void checkClientForSwap(MasterClient client)
        {
            debugWrite(dbgClients, "[Clients] - Flags state of client {0}: {1}, {2}, {3}", client.GmClient.Name, client.IsNoSync, client.IsSyncToStaging, client.IsSyncToTeam);
            // Do not proceed if the client is not in either server or if the client is a spectator or client is nosync.
            if (!client.HasGmClient || !client.HasTsClient || client.GmClient.TeamId == 0 || client.IsNoSync)
                return;

            // Used for debug print.
            Int32 channelId = client.TsClient.medChannelId.Value;

            // Move To Staging Channel If:
            //   Team Based swapping is off, or
            //   The number of players is lower than the team swapping threshold, or
            //   Intermission swapping is on and the game is in intermission.
            if (!synTeamBasedSwapping || getPlayersOnBothServers().Count < synTeamBasedThreshold || (synIntermissionSwapping && mBetweenRounds))
            {
                // Don't move players from pickup channels to the staging channel.
                foreach (TeamspeakChannel tsChannel in mPickupChannels)
                    if (tsChannel.tsId == client.TsClient.medChannelId)
                    {
                        consoleWrite("[Swapping] - Staging Mode - Skipping Client ({0}) because he/she is in Ch.{1}.", client.TsClient.tsName, client.TsClient.medChannelId);
                        break;
                    }

                // Move the client to the staging channel.
                if (client.TsClient.medChannelId != mStagingChannel.tsId)
                {
                    sendTeamspeakQuery(TeamspeakQuery.buildClientMoveQuery(client.TsClient.tsId.Value, mStagingChannel.tsId.Value));
                    if (!performResponseHandling(Queries.CheckSwapStaging)) return;
                    client.TsClient.medChannelId = mStagingChannel.tsId;
                    debugWrite(dbgSwapping, "[Swapping] - Staging Mode - Client ({0}) from Ch.{1} to Ch.{2}.", client.TsClient.tsName, channelId, client.TsClient.medChannelId);

                    // Check if channels need to be deleted if the option is set.
                    if (chnRemoveOnEmpty)
                        removeChannels();
                }
            }
            // Move To Team Channel If:
            //   Squad Based swapping is off, or
            //   The number of players is less than the squad swapping threshold, or
            //   The player is not in a squad, or
            //   The number of players in the squad is less than the squad swapping minimum, or
            //   The player is marked as Sync to Team
            else if (!synSquadBasedSwapping || getPlayersOnBothServersOnTeam(client.GmClient.TeamId).Count < synSquadBasedThreshold || client.GmClient.SquadId == 0 || getPlayersOnBothServersOnSquad(client.GmClient.TeamId, client.GmClient.SquadId).Count < synSquadSizeMinimum || client.IsSyncToTeam)
            {
                // Locate / Create the team channel.
                if (!mTeamChannels.ContainsKey(client.GmClient.TeamId))
                {
                    findOrCreateTeamChannel(client.GmClient.TeamId);
                    if (!mTeamChannels.ContainsKey(client.GmClient.TeamId)) return;
                }

                // Move the client to the Team channel.
                if (client.TsClient.medChannelId != mTeamChannels[client.GmClient.TeamId].tsId)
                {
                    sendTeamspeakQuery(TeamspeakQuery.buildClientMoveQuery(client.TsClient.tsId.Value, mTeamChannels[client.GmClient.TeamId].tsId.Value));
                    if (mTsResponse.Id == "768")
                    {
                        mTeamChannels.Remove(client.GmClient.TeamId);
                        mSquadChannels.Remove(client.GmClient.TeamId);
                    }
                    if (!performResponseHandling(Queries.CheckSwapTeam)) return;
                    client.TsClient.medChannelId = mTeamChannels[client.GmClient.TeamId].tsId.Value;
                    debugWrite(dbgSwapping, "[Swapping] - Team Mode - Client ({0}) from Ch.{1} to Ch.{2}.", client.TsClient.tsName, channelId, client.TsClient.medChannelId);

                    // Check if channels need to be deleted if the option is set.
                    if (chnRemoveOnEmpty)
                        removeChannels();
                }
            }
            // Move To Squad Channel If:
            //   Well, all other scenarios are out, so it must be this.
            else
            {
                // Locate / Create the team channel.
                if (!mTeamChannels.ContainsKey(client.GmClient.TeamId))
                {
                    findOrCreateTeamChannel(client.GmClient.TeamId);
                    if (!mTeamChannels.ContainsKey(client.GmClient.TeamId)) return;
                }

                // Locate / Create the squad channel.
                if (!mSquadChannels[client.GmClient.TeamId].ContainsKey(client.GmClient.SquadId))
                {
                    findOrCreateSquadChannel(client.GmClient.TeamId, client.GmClient.SquadId);
                    if (!mSquadChannels[client.GmClient.TeamId].ContainsKey(client.GmClient.SquadId)) return;
                }

                // Move the client to the Squad channel
                if (client.TsClient.medChannelId != mSquadChannels[client.GmClient.TeamId][client.GmClient.SquadId].tsId)
                {
                    sendTeamspeakQuery(TeamspeakQuery.buildClientMoveQuery(client.TsClient.tsId.Value, mSquadChannels[client.GmClient.TeamId][client.GmClient.SquadId].tsId.Value));
                    if (mTsResponse.Id == "768")
                    {
                        mSquadChannels[client.GmClient.TeamId].Remove(client.GmClient.SquadId);
                    }
                    if (!performResponseHandling(Queries.CheckSwapSquad)) return;
                    client.TsClient.medChannelId = mSquadChannels[client.GmClient.TeamId][client.GmClient.SquadId].tsId.Value;
                    debugWrite(dbgSwapping, "[Swapping] - Squad Mode - Client ({0}) from Ch.{1} to Ch.{2}.", client.TsClient.tsName, channelId, client.TsClient.medChannelId);

                    // Check if channels need to be deleted if the option is set.
                    if (chnRemoveOnEmpty)
                        removeChannels();
                }
            }
        }
        /// <summary>Removes a client from the attacking/defending channel, if they need to be.</summary>
        /// <param name="client">The client to check.</param>
        public void checkClientForRemove(MasterClient client)
        {
            // Do not proceed if the client is in the game server or not in teamspeak.
            if (client.HasGmClient || !client.HasTsClient)
                return;

            // Used for debug print.
            Int32 channelId = client.TsClient.medChannelId.Value;

            // Only attempt to remove the client if they are not in the whitelist.
            if (!mClientWhitelist.Contains(client.TsClient.medDatabaseId.Value))
            {
                //Do not remove a client if they are in a pickup channel
                foreach (TeamspeakChannel teamspeakChannel in mPickupChannels)
                {
                    if (client.TsClient.medChannelId == teamspeakChannel.tsId)
                    {
                        return;
                    }
                }

                // Determine the channel that we need to move to.  
                int? swapChannelId = ts3EnableDropoff ? mDropoffChannel.tsId : mStagingChannel.tsId;
                
                // Move the client to the appropriate channel (Dropoff or staging)
                if (client.TsClient.medChannelId != swapChannelId)
                {
                    sendTeamspeakQuery(TeamspeakQuery.buildClientMoveQuery(client.TsClient.tsId.Value, swapChannelId.Value));
                    if (!performResponseHandling(Queries.CheckRemove)) return;
                    client.TsClient.medChannelId = swapChannelId;
                    debugWrite(dbgSwapping, "[Swapping] - Remove Client - Client ({0}) from Ch.{1} to Ch.{2}.", client.TsClient.tsName, channelId, client.TsClient.medChannelId);

                    // Delete the channel if the remove option is enabled.
                    if (chnRemoveOnEmpty)
                        removeChannels();
                }
            }
        }
        /// <summary>Messages a client if they aren't in teamspeak.</summary>
        /// <param name="client">The client to check.</param>
        public void checkClientForMessage(MasterClient client)
        {
            // Do not proceed if the client is in teamspeak or not in bad company 2.
            if (client.HasTsClient || !client.HasGmClient)
                return;
            yellToPlayer(msgMessage, msgDuration, client.GmClient.Name);
        }



        /// <summary>Messages a client after a set delay if they aren't on teamspeak.</summary>
        /// <param name="Name">The name of the client who just joined.</param>
        public void playerJoined(String Name)
        {
            if (msgEnabled && msgOnJoin)
                mMessageTimers.Enqueue(new Timer(new TimerCallback(MessageOnJoinCallback), Name, msgOnJoinDelay, Timeout.Infinite));
        }
        /// <summary>Removes the client from the game client list as well as the master client list.</summary>
        /// <param name="Name">The name of the player that left the game server.</param>
        public void playerLeft(String Name)
        {
            // Remove the client from the game client list.
            foreach (GameClient gmClient in mClientGmInfo)
                if (gmClient.Name == Name)
                {
                    mClientGmInfo.Remove(gmClient);
                    break;
                }

            // Remove the client from the master client list.
            foreach (MasterClient mstClient in mClientAllInfo)
                if (mstClient.HasGmClient && mstClient.GmClient.Name == Name)
                {
                    mstClient.GmClient = null;
                    if (!mstClient.HasTsClient)
                        mClientAllInfo.Remove(mstClient);
                    else
                        addToActionQueue(Commands.CheckClientForRemoval, mstClient);
                    break;
                }
        }

        /// <summary>Event Bouncer: Checks if the player is in a staging/team/squad channel. If not, it kicks them.</summary>
        /// <param name="Name">The name of the player that spawned.</param>
        public void playerSpawned(String Name)
        {
            if (mEnableBouncer)
            {
                Boolean needsKick = true;
                String kMsg = "";

                foreach (MasterClient mstClient in mClientAllInfo)
                {
                    if (mstClient.HasGmClient && mstClient.GmClient.Name == Name)
                    {
                        // Check Teamspeak Channel Id versus given staging/squad/etc channel ids
                        if (mstClient.HasTsClient)
                        {
                            // Player may not have a TS client yet depending on timing.
                            debugWrite(dbgBouncer, "[BNC] Player " + Name + " has TS Client.");
                            
                            int chanId = mstClient.TsClient.medChannelId.HasValue ?
                                mstClient.TsClient.medChannelId.Value : -1;

                            kMsg += "ChanID: " + chanId + " Checked IDs: " + mStagingChannel.tsId + ",";

                            // Check to see if user is in staging channel.
                            if (chanId == mStagingChannel.tsId)
                                needsKick = false;

                            // Check to see if the user is in a pickup channel.
                            foreach (TeamspeakChannel pickupChannel in mPickupChannels)
                            {
                                kMsg += pickupChannel.tsId + ",";
                                if (chanId == pickupChannel.tsId)
                                {
                                    needsKick = false;
                                    break;
                                }
                            }
                            // Check to see if the user is in a team channel.
                            foreach (TeamspeakChannel teamChannel in mTeamChannels.Values)
                            {
                                kMsg += teamChannel.tsId + ",";
                                if (chanId == teamChannel.tsId)
                                {
                                    needsKick = false;
                                    break;
                                }
                            }

                            // Check to see if the user is in a squad channel.
                            foreach (Dictionary<Int32, TeamspeakChannel> teamChannels in mSquadChannels.Values)
                                foreach (TeamspeakChannel squadChannel in teamChannels.Values)
                                {
                                    kMsg += squadChannel.tsId + ",";
                                    if (chanId == squadChannel.tsId)
                                    {
                                        needsKick = false;
                                        break;
                                    }
                                }
                        }
                        else
                        {
                            // Player doesn't have TS client which probably means s/he is not on the server.
                            needsKick = true;
                        }

                        // Kick Player
                        if (needsKick)
                        {
                            debugWrite(dbgBouncer, "[BNC] Player " + Name + " not found on TS/in proper channel. Kicking.");
                            debugWrite(dbgBouncer, "[BNC] " + kMsg);

                            this.ExecuteCommand("procon.protected.send", "admin.kickPlayer", Name, mBouncerKickMessage);
                        } else {
                            debugWrite(dbgBouncer, "[BNC] Player " + Name + " found. Not Kicking.");
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>Updates the client's Team/Squad information and swaps them if neccessary.</summary>
        /// <param name="Name">The name of the client that swapped.</param>
        /// <param name="TeamId">The team the client swapped to.</param>
        /// <param name="SquadId">The squad the client swapped to.</param>
        public void playerSwappedTeamsOrSquads(String Name, Int32 TeamId, Int32 SquadId)
        {
            // The swap event is called after a player leaves the server for some reason.
            // So to counteract this, check to see if the previous action was this player
            // leaving the server.  If it was this player, exit the method.
            if (mPreviousAction.Command == Commands.PlayerLeft && ((String)mPreviousAction.Argument) == Name)
                return;

            // Actually handle real player swapped events.
            Boolean wasInServer = false;

            // Note: Will not find the player if they just joined.
            // The player will be found when new game client information is
            // sent to PRoCon.
            foreach (MasterClient mstClient in mClientAllInfo)
                if (mstClient.HasGmClient && mstClient.GmClient.Name == Name)
                {
                    // Update the master client list and the game client list with new bad company client information.
                    mstClient.GmClient = new GameClient(new CPlayerInfo(mstClient.GmClient.Name, mstClient.GmClient.Tags, TeamId, SquadId));
                    foreach (GameClient gmClient in mClientGmInfo)
                        if (gmClient.Name == Name)
                        {
                            mClientGmInfo.Remove(gmClient);
                            break;
                        }
                    mClientGmInfo.Add(mstClient.GmClient);

                    // Check for swap if they're in teamspeak.
                    if (mstClient.HasTsClient)
                        addToActionQueue(Commands.CheckClientForSwapping, mstClient);

                    // Done.
                    wasInServer = true;
                    break;
                }

            // Note: Attempts to match on name to avoid requesting PB info
            // more than necessary. Should possibly request PB info in the future.
            if (!wasInServer)
            {
                // On certain occasions, the player may already be in the game client list.
                // If they are in the list, update them (a.k.a. remove them), otherwise, add them to it.
                GameClient gmClient = new GameClient(new CPlayerInfo(Name, null, TeamId, SquadId));
                foreach (GameClient tGmClient in mClientGmInfo)
                    if (tGmClient.Name == Name)
                    {
                        mClientGmInfo.Remove(tGmClient);
                        break;
                    }
                mClientGmInfo.Add(gmClient);

                // Attempt to match the game client based on their name to a teamspeak client.
                MasterClient matchClient = null;
                Double biggestPercent = 0;
                Double currentPercent = 0;
                foreach (MasterClient mstClient in getPlayersOnTsOnly())
                {
                    currentPercent = MasterClient.calcPercentMatch(gmClient.Name, mstClient.TsClient.tsName);
                    if (currentPercent > biggestPercent)
                    {
                        matchClient = mstClient;
                        biggestPercent = currentPercent;
                    }
                }

                // If our match is above the threshold, the clients match.
                // Update the Master client list then check the player for a swap.
                if (biggestPercent >= synMatchingThreshold && matchClient != null)
                {
                    matchClient.GmClient = gmClient;
                    addToActionQueue(Commands.CheckClientForSwapping, matchClient);
                }
                // Otherwise, consider the client not in teamspeak, and add them as BC2 only.
                else
                    mClientAllInfo.Add(new MasterClient(gmClient));
            }
        }



        /// <summary>A combination of findTeamChannel and createTeamChannel.</summary>
        public void findOrCreateTeamChannel(Int32 TeamId)
        {
            // Determine what the team channel should be named.
            String teamChannelName = (chnTeamNames.Length >= TeamId) ? chnTeamNames[TeamId - 1] : "Team " + TeamId;
            debugWrite(dbgChannels, "[Channels] Attempting to find or create ^bTeam^n Channel: {0}.", teamChannelName);

            // Attempt to find the channel first. If that fails, attempt to create it.
            findTeamChannel(teamChannelName, TeamId);
            if (!mTeamChannels.ContainsKey(TeamId))
                createTeamChannel(teamChannelName, TeamId);
        }
        /// <summary>A combination of findSquadChannel and createSquadChannel.</summary>
        public void findOrCreateSquadChannel(Int32 TeamId, Int32 SquadId)
        {
            // Determine what the squad channel should be named.
            String squadChannelName = (chnSquadNames.Length >= SquadId) ? chnSquadNames[SquadId - 1] : "Squad " + SquadId;
            debugWrite(dbgChannels, "[Channels] Attempting to find or create ^bSquad^n Channel: {0}.", squadChannelName);

            // Attempt to find the channel first. If that fails, attempt to create it.
            findSquadChannel(squadChannelName, TeamId, SquadId);
            if (mSquadChannels.ContainsKey(TeamId) && !mSquadChannels[TeamId].ContainsKey(SquadId))
                createSquadChannel(squadChannelName, TeamId, SquadId);
        }
        /// <summary>Checks to see if a channel exists for the specified team.</summary>
        public void findTeamChannel(String Name, Int32 TeamId)
        {
            // Debug Print.
            debugWrite(dbgChannels, "[Channels] Attempting to find ^bTeam^n Channel: {0}.", Name);

            // Request a list of channels on the teamspeak server.
            sendTeamspeakQuery(TeamspeakQuery.buildChannelListQuery());
            if (!performResponseHandling(Queries.FindTeamChannelList))
                return;

            // Create a list of channels out of the response.
            List<TeamspeakChannel> tsChannels = new List<TeamspeakChannel>();
            foreach (TeamspeakResponseSection tsResponseSection in mTsResponse.Sections)
                foreach (TeamspeakResponseGroup tsResponseGroup in tsResponseSection.Groups)
                    tsChannels.Add(new TeamspeakChannel(tsResponseGroup));

            // Filter channels whom don't have the correct name.
            for (int i = 0; i < tsChannels.Count; i++)
                if (tsChannels[i].tsName != Name)
                    tsChannels.RemoveAt(i--);

            // Select the best channel:
            //   Children of the staging channel take precedence.
            //   Children of the root channel are next.
            //   Other channels are not qualified.
            TeamspeakChannel teamChannel = null;
            for (int i = 0; i < tsChannels.Count && teamChannel == null; i++)
                if (tsChannels[i].medPId == mStagingChannel.tsId)
                    teamChannel = tsChannels[i];
            for (int i = 0; i < tsChannels.Count && teamChannel == null; i++)
                if (tsChannels[i].medPId == 0)
                    teamChannel = tsChannels[i];

            // Add the channel to global channels list if we found one.
            if (teamChannel != null)
            {
                mTeamChannels.Remove(TeamId);
                mSquadChannels.Remove(TeamId);
                mTeamChannels.Add(TeamId, teamChannel);
                mSquadChannels.Add(TeamId, new Dictionary<Int32, TeamspeakChannel>());
                debugWrite(dbgChannels, "[Channels] Found ^bTeam^n Channel: {0} ({1}).", teamChannel.tsName, teamChannel.tsId);
            }
        }
        /// <summary>Checks to see if a channel exists for the specified squad.</summary>
        public void findSquadChannel(String Name, Int32 TeamId, Int32 SquadId)
        {
            // Exit if the team channel doesn't exist.
            if (!mTeamChannels.ContainsKey(TeamId) || !mSquadChannels.ContainsKey(TeamId))
                return;

            // Debug print.
            debugWrite(dbgChannels, "[Channels] Attempting to find ^bSquad^n Channel: {0}.", Name);

            // Request a list of channels on the teamspeak server.
            sendTeamspeakQuery(TeamspeakQuery.buildChannelListQuery());
            if (!performResponseHandling(Queries.FindSquadChannelList))
                return;

            // Create a list of channels out of the response.
            List<TeamspeakChannel> tsChannels = new List<TeamspeakChannel>();
            foreach (TeamspeakResponseSection tsResponseSection in mTsResponse.Sections)
                foreach (TeamspeakResponseGroup tsResponseGroup in tsResponseSection.Groups)
                    tsChannels.Add(new TeamspeakChannel(tsResponseGroup));

            // Filter channels whom don't have the correct name.
            for (int i = 0; i < tsChannels.Count; i++)
                if (tsChannels[i].tsName != Name)
                    tsChannels.RemoveAt(i--);

            // Select the best channel:
            //   Only children of the team channels are allowed.
            TeamspeakChannel squadChannel = null;
            for (int i = 0; i < tsChannels.Count && squadChannel == null; i++)
                if (tsChannels[i].medPId == mTeamChannels[TeamId].tsId)
                    squadChannel = tsChannels[i];

            // Add the channel to the global channels list if we found one.
            if (squadChannel != null)
            {
                mSquadChannels[TeamId].Remove(SquadId);
                mSquadChannels[TeamId].Add(SquadId, squadChannel);
                debugWrite(dbgChannels, "[Channels] Found ^bSquad^n Channel: {0} ({1}).", squadChannel.tsName, squadChannel.tsId);
            }
        }
        /// <summary>Attempts to create a channel for the specified team.</summary>
        public void createTeamChannel(String Name, Int32 TeamId)
        {
            // Setup the general portions of the query.
            TeamspeakQuery channelCreateQuery = new TeamspeakQuery("channelcreate");
            channelCreateQuery.addParameter("channel_name", Name);
            channelCreateQuery.addParameter("channel_flag_permanent", "1");
            channelCreateQuery.addParameter("cpid", mStagingChannel.tsId.Value.ToString());
            channelCreateQuery.addParameter( "channel_codec_quality", "10");
            debugWrite(dbgChannels, "[Channels] Attempting to create ^bTeam^n Channel: {0}.", Name);

            // Determine if we should set a password.
            if (chnPassword != String.Empty)
            {
                channelCreateQuery.addParameter("channel_password", chnPassword);
                debugWrite(dbgChannels, "[Channels] Using Password ({0}).", chnPassword);
            }

            // Determine where the channel should be sorted.
            for (int i = TeamId - 1; i >= 0; i--)
                if (mTeamChannels.ContainsKey(i) && mTeamChannels[i].medPId == mStagingChannel.tsId)
                {
                    channelCreateQuery.addParameter("channel_order", mTeamChannels[i].tsId.ToString());
                    debugWrite(dbgChannels, "[Channels] Order After {0}.", mTeamChannels[i].tsName);
                    break;
                }
                else if (i == 0)
                {
                    channelCreateQuery.addParameter("channel_order", "0");
                    debugWrite(dbgChannels, "[Channels] Order After Top.");
                    break;
                }

            // Create the channel.
            sendTeamspeakQuery(channelCreateQuery);
            if (!performResponseHandling(Queries.CreateTeamChannelQuery))
                return;

            // Get the channel's info.
            Int32 channelId = Int32.Parse(mTsResponse.Sections[0].Groups[0]["cid"]);
            sendTeamspeakQuery(TeamspeakQuery.buildChannelInfoQuery(channelId));
            if (!performResponseHandling(Queries.CreateTeamChannelInfo))
                return;

            // Set the channel.
            TeamspeakChannel teamChannel = new TeamspeakChannel(mTsResponse.Sections[0].Groups[0]);
            teamChannel.tsId = channelId;

            // Add the channel to global channels list.
            mTeamChannels.Remove(TeamId);
            mSquadChannels.Remove(TeamId);
            mTeamChannels.Add(TeamId, teamChannel);
            mSquadChannels.Add(TeamId, new Dictionary<Int32, TeamspeakChannel>());
            debugWrite(dbgChannels, "[Channels] Created ^bTeam^n Channel: {0} ({1}).", teamChannel.tsName, teamChannel.tsId);
        }
        /// <summary>Attempts to create a channel for the specified squad.</summary>
        public void createSquadChannel(String Name, Int32 TeamId, Int32 SquadId)
        {
            // Exit if the team channel doesn't exist.
            if (!mTeamChannels.ContainsKey(TeamId) || !mSquadChannels.ContainsKey(TeamId))
                return;

            // Setup the general portions of the query.
            TeamspeakQuery channelCreateQuery = new TeamspeakQuery("channelcreate");
            channelCreateQuery.addParameter("channel_name", Name);
            channelCreateQuery.addParameter("channel_flag_permanent", "1");
            channelCreateQuery.addParameter("cpid", mTeamChannels[TeamId].tsId.Value.ToString());
            debugWrite(dbgChannels, "[Channels] Attempting to create ^bSquad^n Channel: {0}.", Name);

            // Determine if we should set a password.
            if (chnPassword != String.Empty)
            {
                channelCreateQuery.addParameter("channel_password", chnPassword);
                debugWrite(dbgChannels, "[Channels] Using Password ({0}).", chnPassword);
            }

            // Determine where the channel should be sorted.
            for (int i = SquadId - 1; i >= 0; i--)
                if (mSquadChannels[TeamId].ContainsKey(i) && mSquadChannels[TeamId][i].medPId == mTeamChannels[TeamId].tsId)
                {
                    channelCreateQuery.addParameter("channel_order", mSquadChannels[TeamId][i].tsId.ToString());
                    debugWrite(dbgChannels, "[Channels] Order After {0}.", mSquadChannels[TeamId][i].tsName);
                    break;
                }
                else if (i == 0)
                {
                    channelCreateQuery.addParameter("channel_order", "0");
                    debugWrite(dbgChannels, "[Channels] Order After Top.");
                    break;
                }

            // Create the channel.
            sendTeamspeakQuery(channelCreateQuery);
            if (!performResponseHandling(Queries.CreateSquadChannelQuery))
                return;

            // Get the channel's info.
            Int32 channelId = Int32.Parse(mTsResponse.Sections[0].Groups[0]["cid"]);
            sendTeamspeakQuery(TeamspeakQuery.buildChannelInfoQuery(channelId));
            if (!performResponseHandling(Queries.CreateSquadChannelInfo))
                return;

            // Set the channel.
            TeamspeakChannel squadChannel = new TeamspeakChannel(mTsResponse.Sections[0].Groups[0]);
            squadChannel.tsId = channelId;

            // Add the channel to global channels list.
            mSquadChannels[TeamId].Remove(SquadId);
            mSquadChannels[TeamId].Add(SquadId, squadChannel);
            debugWrite(dbgChannels, "[Channels] Created ^bSquad^n Channel: {0} ({1}).", squadChannel.tsName, squadChannel.tsId);
        }
        /// <summary>Attempts to remove each squad channel if it is empty.</summary>
        public void removeChannels()
        {
            // Dictionaries of channels to remove.
            Dictionary<Int32, TeamspeakChannel> teamChannels = new Dictionary<Int32, TeamspeakChannel>();
            Dictionary<Int32, Dictionary<Int32, TeamspeakChannel>> squadChannels = new Dictionary<Int32, Dictionary<Int32, TeamspeakChannel>>();
            debugWrite(dbgChannels, "[Channels] Attempting to remove empty channels.");

            // Build a list of clients in the server.
            List<TeamspeakClient> clientInfo = new List<TeamspeakClient>();
            // Request all the clients basic information.  Bail out if the query was bad.
            sendTeamspeakQuery(TeamspeakQuery.buildClientListQuery());
            if (!performResponseHandling(Queries.RemoveChannelsList))
                return;
            // Build list of clients.
            foreach (TeamspeakResponseSection sec in mTsResponse.Sections)
                foreach (TeamspeakResponseGroup grp in sec.Groups)
                    clientInfo.Add(new TeamspeakClient(grp));

            // Get rid of squad channels that are empty.
            foreach (Int32 teamId in mSquadChannels.Keys)
                foreach (Int32 squadId in mSquadChannels[teamId].Keys)
                {
                    // Check if there are users in the channel.
                    Boolean inChannel = false;
                    foreach (TeamspeakClient tsClient in clientInfo)
                        if (inChannel = mSquadChannels[teamId][squadId].tsId == tsClient.medChannelId)
                            break;
                    // Add the channel to the list of channels to remove.
                    if (!inChannel)
                    {
                        if (!squadChannels.ContainsKey(teamId))
                            squadChannels.Add(teamId, new Dictionary<Int32, TeamspeakChannel>());
                        squadChannels[teamId].Add(squadId, mSquadChannels[teamId][squadId]);
                    }
                }

            // Get rid of team channels that are empty.
            //   This only applies to children of the staging channel.
            foreach (Int32 teamId in mTeamChannels.Keys)
                if (mTeamChannels[teamId].medPId == mStagingChannel.tsId)
                {
                    // Check if there are users in the channel.
                    Boolean inChannel = false;
                    foreach (TeamspeakClient tsClient in clientInfo)
                        if (inChannel = mTeamChannels[teamId].tsId == tsClient.medChannelId)
                            break;
                    // Add the channel to the list of channels to remove.
                    if (!inChannel)
                        teamChannels.Add(teamId, mTeamChannels[teamId]);
                }

            // Remove squad channels that we marked as empty.
            foreach (Int32 teamId in squadChannels.Keys)
                foreach (Int32 squadId in squadChannels[teamId].Keys)
                {
                    TeamspeakQuery deleteChannel = new TeamspeakQuery("channeldelete");
                    deleteChannel.addParameter("cid", mSquadChannels[teamId][squadId].tsId.ToString());
                    deleteChannel.addParameter("force", "1");
                    sendTeamspeakQuery(deleteChannel);
                    if (!performResponseHandling(Queries.RemoveChannelsSquadQuery)) return;
                    if (mTsResponse.Id != "0") continue;
                    debugWrite(dbgChannels, "[Channels] Removed ^bSquad^n Channel: {0} ({1}).", mSquadChannels[teamId][squadId].tsName, mSquadChannels[teamId][squadId].tsId);
                    mSquadChannels[teamId].Remove(squadId);
                }

            // Remove team channels we marked as empty.
            //   Double check to make sure all sub-channels are empty.
            foreach (Int32 teamId in teamChannels.Keys)
                if (!mSquadChannels.ContainsKey(teamId) || mSquadChannels[teamId].Count == 0)
                {
                    TeamspeakQuery deleteChannel = new TeamspeakQuery("channeldelete");
                    deleteChannel.addParameter("cid", mTeamChannels[teamId].tsId.ToString());
                    deleteChannel.addParameter("force", "1");
                    sendTeamspeakQuery(deleteChannel);
                    if (!performResponseHandling(Queries.RemoveChannelsTeamQuery)) return;
                    if (mTsResponse.Id != "0") continue;
                    debugWrite(dbgChannels, "[Channels] Removed ^bTeam^n Channel: {0} ({1}).", mTeamChannels[teamId].tsName, mTeamChannels[teamId].tsId);
                    mTeamChannels.Remove(teamId);
                    mSquadChannels.Remove(teamId);
                }

            // Debug print.
            debugWrite(dbgChannels, "[Channels] Done removing empty channels.");
        }



        /// <summary>Retrieves a list of players that are on both the teamspeak 3 and game server for a specific team.</summary>
        /// <returns>A list of players on both servers for the specified team.</returns>
        public List<MasterClient> getPlayersOnBothServersOnSquad(Int32 teamId, Int32 squadId)
        {
            List<MasterClient> returnList = new List<MasterClient>();
            foreach (MasterClient masterClient in mClientAllInfo)
                if (masterClient.HasGmClient && masterClient.HasTsClient)
                    if (masterClient.GmClient.TeamId == teamId && masterClient.GmClient.SquadId == squadId)
                        returnList.Add(masterClient);
            return returnList;
        }
        /// <summary>Retrieves a list of players that are on both the teamspeak 3 and game server for a specific team.</summary>
        /// <returns>A list of players on both servers for the specified team.</returns>
        public List<MasterClient> getPlayersOnBothServersOnTeam(Int32 teamId)
        {
            List<MasterClient> returnList = new List<MasterClient>();
            foreach (MasterClient masterClient in mClientAllInfo)
                if (masterClient.HasGmClient && masterClient.HasTsClient)
                    if (masterClient.GmClient.TeamId == teamId)
                        returnList.Add(masterClient);
            return returnList;
        }
        /// <summary>Retrieves a list of players that are on both the teamspeak 3 and the game server.</summary>
        /// <returns>A list of players on both servers.</returns>
        public List<MasterClient> getPlayersOnBothServers()
        {
            List<MasterClient> returnList = new List<MasterClient>();
            foreach (MasterClient masterClient in mClientAllInfo)
                if (masterClient.HasGmClient && masterClient.HasTsClient)
                    returnList.Add(masterClient);
            return returnList;
        }
        /// <summary>Retrieves a list of players that are only on the teamspeak 3 server.</summary>
        /// <returns>A list of players only on the teamspeak server.</returns>
        public List<MasterClient> getPlayersOnTsOnly()
        {
            List<MasterClient> returnList = new List<MasterClient>();
            foreach (MasterClient masterClient in mClientAllInfo)
                if (!masterClient.HasGmClient && masterClient.HasTsClient)
                    returnList.Add(masterClient);
            return returnList;
        }
        /// <summary>Retrieves a list of players that are only on the game server.</summary>
        /// <returns>A list of players only on the game server.</returns>
        public List<MasterClient> getPlayersOnBcOnly()
        {
            List<MasterClient> returnList = new List<MasterClient>();
            foreach (MasterClient masterClient in mClientAllInfo)
                if (masterClient.HasGmClient && !masterClient.HasTsClient)
                    returnList.Add(masterClient);
            return returnList;
        }
        /// <summary>Retrieves a list of players that are on the teamspeak server.</summary>
        /// <returns>A list of players on the teamspeak server.</returns>
        public List<MasterClient> getPlayersOnTs()
        {
            List<MasterClient> returnList = new List<MasterClient>();
            foreach (MasterClient masterClient in mClientAllInfo)
                if (masterClient.HasTsClient)
                    returnList.Add(masterClient);
            return returnList;
        }
        /// <summary>Retrieves a list of players that are on the game server.</summary>
        /// <returns>A list of players on the game server.</returns>
        public List<MasterClient> getPlayersOnBc()
        {
            List<MasterClient> returnList = new List<MasterClient>();
            foreach (MasterClient masterClient in mClientAllInfo)
                if (masterClient.HasGmClient)
                    returnList.Add(masterClient);
            return returnList;
        }



        /// <summary>Adds a command to the action queue with the specified arguments.</summary>
        public void addToActionQueue(Commands command, params Object[] arguments)
        {
            mActionMutex.WaitOne();
            if (command == Commands.PluginEnabled || command == Commands.PluginDisabled)
            {
                // Remove all previous enabled/disabled commands.
                Queue<ActionEvent> tNew = new Queue<ActionEvent>();
                while (mActions.Count > 0 && (mActions.Peek().Command == Commands.PluginEnabled || mActions.Peek().Command == Commands.PluginDisabled))
                    tNew.Enqueue(mActions.Dequeue());

                // Determine whether we should release a semaphore.
                Boolean tRelease = tNew.Count == 0;

                // Put the new enabled/disabled command on the front.
                tNew.Clear();
                tNew.Enqueue(new ActionEvent(command, arguments));
                while (mActions.Count > 0)
                    tNew.Enqueue(mActions.Dequeue());
                mActions = tNew;

                // Release the semaphore.
                if (tRelease)
                    mActionSemaphore.Release();
            }
            else
            {
                mActions.Enqueue(new ActionEvent(command, arguments));
                mActionSemaphore.Release();
            }
            mActionMutex.ReleaseMutex();
        }
        /// <summary>Sends a query to the teamspeak server (delayed if necessary) and sets the response.</summary>
        public void sendTeamspeakQuery(TeamspeakQuery query)
        {
            if (synDelayQueries)
            {
                TimeSpan delay = TimeSpan.FromMilliseconds(synDelayQueriesAmount);
                TimeSpan delta = DateTime.Now - mTsPrevSendTime;
                if (delta <= delay) Thread.Sleep(delay - delta);
            }
            mTsResponse = mTsConnection.send(query);
            mTsPrevSendTime = DateTime.Now;
        }
        /// <summary>Finds all TS squads that are not full with players on TS and reports this to the player. </summary>
        public void DisplayTsSquadList(string playerName)
        {
            debugWrite(dbgEvents, "[Event] Displaying TS squad list for " + playerName);
            string[] squadNames =
                {"No Squad","Alpha","Bravo","Charlie","Delta","Echo","Foxtrot","Golf","Hotel","India","Juliet","Kilo","Lima","Mike","November","Oscar","Papa","Quebec","Romeo","Sierra","Tango","Uniform","Victor","Whiskey","Xray","Yankee","Zulu"};
            //Find the player's team.  
            int playerTeam = -1;
            //First key is team ID, second key is squad ID.  Inner value is the squad player count info.  
            Dictionary<int, Dictionary<int, TsGameSquadInfo>> squadInfo = new Dictionary<int, Dictionary<int, TsGameSquadInfo>>();
            
            foreach(MasterClient client in mClientAllInfo)
            {
                if(client.HasGmClient)
                {
                    int clientTeam = client.GmClient.TeamId;
                    int clientSquad = client.GmClient.SquadId;
                    
                    //If we've found the callee, save this person's team id.  
                    if(client.GmClient.Name == playerName)
                    {
                        playerTeam = clientTeam;
                    }

                    //Make sure dictionary has all appropriate objects.
                    if(!squadInfo.ContainsKey(clientTeam))
                    {
                        debugWrite(dbgEvents, "[Event] Creating team " + clientTeam);
                        squadInfo[clientTeam] = new Dictionary<int, TsGameSquadInfo>();
                    }
                    if(!squadInfo[clientTeam].ContainsKey(clientSquad))
                    {
                        debugWrite(dbgEvents, "[Event] Creating squad " + clientSquad + " for team " + clientTeam);
                        squadInfo[clientTeam][clientSquad] = new TsGameSquadInfo();
                    }

                    squadInfo[clientTeam][clientSquad].InGameCount++;
                    if(client.HasTsClient)
                    {
                        squadInfo[clientTeam][clientSquad].TsCount++;
                    }
                }
            }
            
            if(playerTeam != -1)
            {
                const string squadMessage = "{0}: ({1}/{2})";
                List<string> messagesToSend = new List<string>();
                //Get the squad list for the appropriate team.  Ensure at least 1 person on TS, and at least 1 free slot.  
                Dictionary<int, TsGameSquadInfo> squads = squadInfo[playerTeam];
                bool squadFound = false;
                foreach(KeyValuePair<int, TsGameSquadInfo> teamSquad in squads)
                {
                    debugWrite(dbgEvents, "[Event] Squad " + squadNames[teamSquad.Key] + ", TS: "+ teamSquad.Value.TsCount + ", Game: "+ teamSquad.Value.InGameCount );
                    if(teamSquad.Value.TsCount > 0 && teamSquad.Value.TsCount < 4)
                    {
                        squadFound = true;
                        string message = String.Format(squadMessage, squadNames[teamSquad.Key], teamSquad.Value.TsCount,
                                      teamSquad.Value.InGameCount);
                        messagesToSend.Add(message);
                    }
                }
                //If there are no squads, tell the player so.  Otherwise, write the message to the player.  
                if(!squadFound)
                {
                    sayToPlayer("No free squads found. Start one yourself and encourage people to join!", playerName);
                }
                else
                {
                    sayToPlayer("Squads with 1-3 Teamspeak Players:", playerName);
                    sayToPlayer("Key: Name (# TS Players/# Squad Members)", playerName);
                    string finalMessageString = "";
                    for(int i = 0; i<messagesToSend.Count; i++)
                    {
                        finalMessageString += messagesToSend[i];
                        if(i != (messagesToSend.Count-1))
                        {
                            finalMessageString += ", ";
                        }
                    }
                    sayToPlayer(finalMessageString, playerName);
                }
            }

        }
        /// <summary>
        /// Sets all users to have no special sync flags.  
        /// </summary>
        public void ResetAllUserSyncFlags()
        {
            foreach(MasterClient user in mClientAllInfo)
            {
                user.IsSyncToTeam = false;
                user.IsSyncToStaging = false;
                user.IsNoSync = false;
                
            }
        }
        /// <summary>Sets the NoSync flag for a player on the server.  This player will be ignored by Teamsync until the next round or until the flag is reset. </summary>
        public void SetNoSyncFlagForPlayer(string playerName)
        {
            foreach (MasterClient user in mClientAllInfo)
            {
                if(user.HasGmClient && user.GmClient.Name == playerName)
                {
                    user.IsNoSync = true;
                    user.IsSyncToStaging = false;
                    user.IsSyncToTeam = false;
                    sayToPlayer("Squad sync disabled for you.", user.GmClient.Name);
                    sayToPlayer("Type !tssync to re-enable squad sync.", user.GmClient.Name);
                    sayToPlayer("Squad sync will automatically re-enable at round end.", user.GmClient.Name);
                    
                    break;
                }
            }
        }
        /// <summary>Sets the Sync to Team flag for a player on the server.  This player will be kept in the team channel until the next round or until the flag is reset. </summary>
        public void SetSyncToTeamFlagForPlayer(string playerName)
        {
            foreach (MasterClient user in mClientAllInfo)
            {
                if (user.HasGmClient && user.GmClient.Name == playerName)
                {
                    user.IsNoSync = false;
                    user.IsSyncToTeam = true;
                    user.IsSyncToStaging = false;
                    sayToPlayer("Moving you to your Team channel.", user.GmClient.Name);
                    sayToPlayer("Type !tssync to re-enable squad sync.", user.GmClient.Name);
                    sayToPlayer("Squad sync will automatically re-enable at round end.", user.GmClient.Name);
                    addToActionQueue(Commands.CheckClientForSwapping, user);
                    
                    break;
                }
            }

        }
        /// <summary>Sets the Sync to Staging flag for a player on the server.  This player will be kept in the staging channel until the next round or until the flag is reset. </summary>
        public void SetSyncToStagingFlagForPlayer(string playerName)
        {
            foreach (MasterClient user in mClientAllInfo)
            {
                if (user.HasGmClient && user.GmClient.Name == playerName)
                {
                    user.IsNoSync = false;
                    user.IsSyncToTeam = false;
                    user.IsSyncToStaging = true;
                    sayToPlayer("Moving you to the TeamSpeak lobby.", user.GmClient.Name);
                    sayToPlayer("Type !tssync to re-enable squad sync.", user.GmClient.Name);
                    sayToPlayer("Squad sync will automatically re-enable at round end.", user.GmClient.Name);
                    addToActionQueue(Commands.CheckClientForSwapping, user);
                    break;
                }
            }
            
        }
        /// <summary>Sets all player Sync flags to false.  This resumes default TSSync behavior.</summary>
        public void ResetSyncFlagsForPlayer(string playerName)
        {
            foreach (MasterClient user in mClientAllInfo)
            {
                if (user.HasGmClient && user.GmClient.Name == playerName)
                {
                    user.IsNoSync = false;
                    user.IsSyncToTeam = false;
                    user.IsSyncToStaging = false;

                    addToActionQueue(Commands.CheckClientForSwapping, user);
                    sayToPlayer("Squad sync re-enabled.", user.GmClient.Name);
                    //Following line not needed with new TS settings. 
					//sayToPlayer("Squad sync only functions with 6+ TS players.", user.GmClient.Name);
                    break;
                }
            }

        }
    }
}