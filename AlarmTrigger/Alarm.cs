using Microsoft.AspNet.SignalR.Client;
using DBHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

namespace AlarmTrigger
{
    class Alarm
    {
        
        private static List<DeviceInfo> deviceInfos = new List<DeviceInfo>();
        private static Int32 m_lUserID = -1;
        private static Int32[] m_lAlarmHandle = new Int32[200];
        private static Int32 iListenHandle = -1;
        private static int iDeviceNumber = 0; //添加设备个数
        private static int iFileNumber = 0; //保存的文件个数
        private static uint iLastErr = 0;
        private static string strErr;
        private static IHubProxy proxy;
        private static HubConnection con;
        private static Timer _timer;
        public static CHCNetSDK.LOGINRESULTCALLBACK LoginCallBack = null;
        private static CHCNetSDK.EXCEPYIONCALLBACK m_fExceptionCB = null;
        private static CHCNetSDK.MSGCallBack_V31 m_falarmData_V31 = null;
        private static CHCNetSDK.MSGCallBack m_falarmData = null;

        public delegate void UpdateTextStatusCallback(string strLogStatus, IntPtr lpDeviceInfo);
        public delegate void UpdateListBoxCallback(string strAlarmTime, string strDevIP, string strAlarmMsg);
        public delegate void UpdateListBoxCallbackException(string strAlarmTime, int lUserID, string strAlarmMsg);

        //CHCNetSDK.NET_VCA_TRAVERSE_PLANE m_struTraversePlane = new CHCNetSDK.NET_VCA_TRAVERSE_PLANE();
        CHCNetSDK.NET_VCA_AREA m_struVcaArea = new CHCNetSDK.NET_VCA_AREA();
        CHCNetSDK.NET_VCA_INTRUSION m_struIntrusion = new CHCNetSDK.NET_VCA_INTRUSION();
        CHCNetSDK.UNION_STATFRAME m_struStatFrame = new CHCNetSDK.UNION_STATFRAME();
        CHCNetSDK.UNION_STATTIME m_struStatTime = new CHCNetSDK.UNION_STATTIME();
        public  Alarm() {
            ConnectToHub();
            
            bool m_bInitSDK = CHCNetSDK.NET_DVR_Init();
            if (m_bInitSDK == false)
            {
                Console.WriteLine("NET_DVR_Init error!");
                return;
            }
            else
            {
                byte[] strIP = new byte[16 * 16];
                uint dwValidNum = 0;
                Boolean bEnableBind = false;

                //获取本地PC网卡IP信息
                if (CHCNetSDK.NET_DVR_GetLocalIP(strIP, ref dwValidNum, ref bEnableBind))
                {
                    if (dwValidNum > 0)
                    {
                        //取第一张网卡的IP地址为默认监听端口
                        Console.WriteLine(System.Text.Encoding.UTF8.GetString(strIP, 0, 16));
                        //CHCNetSDK.NET_DVR_SetValidIP(0,true); //绑定第一张网卡
                    }
                }

                //保存SDK日志 To save the SDK log
                CHCNetSDK.NET_DVR_SetLogToFile(3, "C:\\SdkLog\\", true);

                //设置透传报警信息类型
                CHCNetSDK.NET_DVR_LOCAL_GENERAL_CFG struLocalCfg = new CHCNetSDK.NET_DVR_LOCAL_GENERAL_CFG();
                struLocalCfg.byAlarmJsonPictureSeparate = 1;//控制JSON透传报警数据和图片是否分离，0-不分离(COMM_VCA_ALARM返回)，1-分离（分离后走COMM_ISAPI_ALARM回调返回）

                Int32 nSize = Marshal.SizeOf(struLocalCfg);
                IntPtr ptrLocalCfg = Marshal.AllocHGlobal(nSize);
                Marshal.StructureToPtr(struLocalCfg, ptrLocalCfg, false);

                if (!CHCNetSDK.NET_DVR_SetSDKLocalCfg(17, ptrLocalCfg))  //NET_DVR_LOCAL_CFG_TYPE_GENERAL
                {
                    iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                    strErr = "NET_DVR_SetSDKLocalCfg failed, error code= " + iLastErr;
                    Console.WriteLine(strErr);
                }
                Marshal.FreeHGlobal(ptrLocalCfg);

                for (int i = 0; i < 200; i++)
                {
                    m_lAlarmHandle[i] = -1;
                }

                //设置异常消息回调函数
                if (m_fExceptionCB == null)
                {
                    m_fExceptionCB = new CHCNetSDK.EXCEPYIONCALLBACK(cbExceptionCB);
                }
                CHCNetSDK.NET_DVR_SetExceptionCallBack_V30(0, IntPtr.Zero, m_fExceptionCB, IntPtr.Zero);

                //设置报警回调函数
                if (m_falarmData_V31 == null)
                {
                    m_falarmData_V31 = new CHCNetSDK.MSGCallBack_V31(MsgCallback_V31);
                }
                CHCNetSDK.NET_DVR_SetDVRMessageCallBack_V31(m_falarmData_V31, IntPtr.Zero);
            }
            StartListenTcp();
           // GetDevices();
        }
        public void UpdateClientListException(string strAlarmTime, int lUserID, string strAlarmMsg)
        {
            //异常设备信息
            string strDevIP = "";
            strDevIP= deviceInfos.Where(x => x.MuserId == lUserID)
                .Select(y => y.DeviceIp.TrimEnd('\0')).FirstOrDefault();
            //for (int i = 0; i < iDeviceNumber; i++)
            //{
            //    m_lUserID = Int32.Parse(DeviceInfo.Items[i].SubItems[0].Text);
            //    if (m_lUserID == lUserID)
            //    {
            //        strDevIP = listViewDevice.Items[i].SubItems[1].Text.TrimEnd('\0');
            //    }
            //}

            //列表新增报警信息
            GetDeviceDetails gt = new GetDeviceDetails();
            gt.SaveMonitoringDetails(strDevIP, strAlarmMsg, strAlarmTime);
            var alarmInfoObj = new AlarmInfo() { AlarmTime = strAlarmTime, DeviceIp = strDevIP, AlarmMsg = strAlarmMsg };
            SendMessageToWeb(alarmInfoObj);
            //listAlarmInfo.Add();
            //listViewAlarmInfo.Items.Add(new ListViewItem(new string[] { strAlarmTime, strDevIP, strAlarmMsg }));

        }

        public void cbExceptionCB(uint dwType, int lUserID, int lHandle, IntPtr pUser)
        {
            //异常消息信息类型
            string stringAlarm = "异常消息回调，信息类型：0x" + Convert.ToString(dwType, 16) + ", lUserID:" + lUserID + ", lHandle:" + lHandle;
            UpdateClientListException(DateTime.Now.ToString(), lUserID, stringAlarm);
        }
        public bool MsgCallback_V31(int lCommand, ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            //通过lCommand来判断接收到的报警信息类型，不同的lCommand对应不同的pAlarmInfo内容
            AlarmMessageHandle(lCommand, ref pAlarmer, pAlarmInfo, dwBufLen, pUser);

            return true; //回调函数需要有返回，表示正常接收到数据
        }

        public static void MsgCallback(int lCommand, ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            //通过lCommand来判断接收到的报警信息类型，不同的lCommand对应不同的pAlarmInfo内容
            AlarmMessageHandle(lCommand, ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
        }
        public static void AlarmMessageHandle(int lCommand, ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            //通过lCommand来判断接收到的报警信息类型，不同的lCommand对应不同的pAlarmInfo内容
            switch (lCommand)
            {
                case CHCNetSDK.COMM_ALARM: //(DS-8000老设备)移动侦测、视频丢失、遮挡、IO信号量等报警信息
                    ProcessCommAlarm(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ALARM_V30://移动侦测、视频丢失、遮挡、IO信号量等报警信息
                    ProcessCommAlarm_V30(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ALARM_RULE://进出区域、入侵、徘徊、人员聚集等行为分析报警信息
                    ProcessCommAlarm_RULE(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_UPLOAD_PLATE_RESULT://交通抓拍结果上传(老报警信息类型)
                    ProcessCommAlarm_Plate(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ITS_PLATE_RESULT://交通抓拍结果上传(新报警信息类型)
                    ProcessCommAlarm_ITSPlate(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ALARM_TPS_REAL_TIME://交通抓拍结果上传(新报警信息类型)
                    ProcessCommAlarm_TPSRealInfo(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ALARM_TPS_STATISTICS://交通抓拍结果上传(新报警信息类型)
                    ProcessCommAlarm_TPSStatInfo(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ALARM_PDC://客流量统计报警信息
                    ProcessCommAlarm_PDC(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ITS_PARK_VEHICLE://客流量统计报警信息
                    ProcessCommAlarm_PARK(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_DIAGNOSIS_UPLOAD://VQD报警信息
                    ProcessCommAlarm_VQD(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_UPLOAD_FACESNAP_RESULT://人脸抓拍结果信息
                    ProcessCommAlarm_FaceSnap(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_SNAP_MATCH_ALARM://人脸比对结果信息
                    ProcessCommAlarm_FaceMatch(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ALARM_FACE_DETECTION://人脸侦测报警信息
                    ProcessCommAlarm_FaceDetect(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ALARMHOST_CID_ALARM://报警主机CID报警上传
                    ProcessCommAlarm_CIDAlarm(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_UPLOAD_VIDEO_INTERCOM_EVENT://可视对讲事件记录信息
                    ProcessCommAlarm_InterComEvent(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ALARM_ACS://门禁主机报警上传
                    ProcessCommAlarm_AcsAlarm(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ID_INFO_ALARM://身份证刷卡信息上传
                    ProcessCommAlarm_IDInfoAlarm(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_UPLOAD_AIOP_VIDEO://设备支持AI开放平台接入，上传视频检测数据
                    ProcessCommAlarm_AIOPVideo(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_UPLOAD_AIOP_PICTURE://设备支持AI开放平台接入，上传图片检测数据
                    ProcessCommAlarm_AIOPPicture(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                case CHCNetSDK.COMM_ISAPI_ALARM://ISAPI报警信息上传
                    ProcessCommAlarm_ISAPIAlarm(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                default:
                    {
                        //报警设备IP地址
                        string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

                        //报警信息类型
                        string stringAlarm = "报警上传，信息类型：0x" + Convert.ToString(lCommand, 16);

                        
                            //创建该控件的主线程直接更新信息列表 
                            UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
                        
                    }
                    break;
            }
        }

        public static void ProcessCommAlarm(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_ALARMINFO struAlarmInfo = new CHCNetSDK.NET_DVR_ALARMINFO();

            struAlarmInfo = (CHCNetSDK.NET_DVR_ALARMINFO)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_ALARMINFO));

            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');
            string stringAlarm = "";
            int i = 0;

            switch (struAlarmInfo.dwAlarmType)
            {
                case 0:
                    stringAlarm = "信号量报警，报警报警输入口：" + struAlarmInfo.dwAlarmInputNumber + "，触发录像通道：";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM; i++)
                    {
                        if (struAlarmInfo.dwAlarmRelateChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 1:
                    stringAlarm = "硬盘满，报警硬盘号：";
                    for (i = 0; i < CHCNetSDK.MAX_DISKNUM; i++)
                    {
                        if (struAlarmInfo.dwDiskNumber[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 2:
                    stringAlarm = "信号丢失，报警通道：";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM; i++)
                    {
                        if (struAlarmInfo.dwChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 3:
                    stringAlarm = "移动侦测，报警通道：";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM; i++)
                    {
                        if (struAlarmInfo.dwChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 4:
                    stringAlarm = "硬盘未格式化，报警硬盘号：";
                    for (i = 0; i < CHCNetSDK.MAX_DISKNUM; i++)
                    {
                        if (struAlarmInfo.dwDiskNumber[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 5:
                    stringAlarm = "读写硬盘出错，报警硬盘号：";
                    for (i = 0; i < CHCNetSDK.MAX_DISKNUM; i++)
                    {
                        if (struAlarmInfo.dwDiskNumber[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 6:
                    stringAlarm = "遮挡报警，报警通道：";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM; i++)
                    {
                        if (struAlarmInfo.dwChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 7:
                    stringAlarm = "制式不匹配，报警通道";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM; i++)
                    {
                        if (struAlarmInfo.dwChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 8:
                    stringAlarm = "非法访问";
                    break;
                default:
                    stringAlarm = "其他未知报警信息";
                    break;
            }

            
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_V30(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {

            CHCNetSDK.NET_DVR_ALARMINFO_V30 struAlarmInfoV30 = new CHCNetSDK.NET_DVR_ALARMINFO_V30();

            struAlarmInfoV30 = (CHCNetSDK.NET_DVR_ALARMINFO_V30)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_ALARMINFO_V30));

            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');
            string stringAlarm = "";
            int i;

            switch (struAlarmInfoV30.dwAlarmType)
            {
                case 0:
                    stringAlarm = "信号量报警，报警报警输入口：" + struAlarmInfoV30.dwAlarmInputNumber + "，触发录像通道：";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byAlarmRelateChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + "\\";
                        }
                    }
                    break;
                case 1:
                    stringAlarm = "硬盘满，报警硬盘号：";
                    for (i = 0; i < CHCNetSDK.MAX_DISKNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byDiskNumber[i] == 1)
                        {
                            stringAlarm += (i + 1) + " ";
                        }
                    }
                    break;
                case 2:
                    stringAlarm = "信号丢失，报警通道：";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 3:
                    stringAlarm = "移动侦测，报警通道：";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 4:
                    stringAlarm = "硬盘未格式化，报警硬盘号：";
                    for (i = 0; i < CHCNetSDK.MAX_DISKNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byDiskNumber[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 5:
                    stringAlarm = "读写硬盘出错，报警硬盘号：";
                    for (i = 0; i < CHCNetSDK.MAX_DISKNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byDiskNumber[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 6:
                    stringAlarm = "遮挡报警，报警通道：";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 7:
                    stringAlarm = "制式不匹配，报警通道";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 8:
                    stringAlarm = "非法访问";
                    break;
                case 9:
                    stringAlarm = "视频信号异常，报警通道";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 10:
                    stringAlarm = "录像/抓图异常，报警通道";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 11:
                    stringAlarm = "智能场景变化，报警通道";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 12:
                    stringAlarm = "阵列异常";
                    break;
                case 13:
                    stringAlarm = "前端/录像分辨率不匹配，报警通道";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                case 15:
                    stringAlarm = "智能侦测，报警通道";
                    for (i = 0; i < CHCNetSDK.MAX_CHANNUM_V30; i++)
                    {
                        if (struAlarmInfoV30.byChannel[i] == 1)
                        {
                            stringAlarm += (i + 1) + " \\ ";
                        }
                    }
                    break;
                default:
                    stringAlarm = "其他未知报警信息";
                    break;
            }

           
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            

        }

        private static void ProcessCommAlarm_RULE(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_VCA_RULE_ALARM struRuleAlarmInfo = new CHCNetSDK.NET_VCA_RULE_ALARM();
            struRuleAlarmInfo = (CHCNetSDK.NET_VCA_RULE_ALARM)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_VCA_RULE_ALARM));

            //报警信息
            string stringAlarm = "";
            uint dwSize = (uint)Marshal.SizeOf(struRuleAlarmInfo.struRuleInfo.uEventParam);

            switch (struRuleAlarmInfo.struRuleInfo.wEventTypeEx)
            {
                case (ushort)CHCNetSDK.VCA_RULE_EVENT_TYPE_EX.ENUM_VCA_EVENT_TRAVERSE_PLANE:
                    IntPtr ptrTraverseInfo = Marshal.AllocHGlobal((Int32)dwSize);
                    Marshal.StructureToPtr(struRuleAlarmInfo.struRuleInfo.uEventParam, ptrTraverseInfo, false);
                    CHCNetSDK.NET_VCA_TRAVERSE_PLANE m_struTraversePlane = (CHCNetSDK.NET_VCA_TRAVERSE_PLANE)Marshal.PtrToStructure(ptrTraverseInfo, typeof(CHCNetSDK.NET_VCA_TRAVERSE_PLANE));
                    stringAlarm = "穿越警戒面，目标ID：" + struRuleAlarmInfo.struTargetInfo.dwID;
                    //警戒面边线起点坐标: (m_struTraversePlane.struPlaneBottom.struStart.fX, m_struTraversePlane.struPlaneBottom.struStart.fY)
                    //警戒面边线终点坐标: (m_struTraversePlane.struPlaneBottom.struEnd.fX, m_struTraversePlane.struPlaneBottom.struEnd.fY)
                    break;
                case (ushort)CHCNetSDK.VCA_RULE_EVENT_TYPE_EX.ENUM_VCA_EVENT_ENTER_AREA:
                    IntPtr ptrEnterInfo = Marshal.AllocHGlobal((Int32)dwSize);
                    Marshal.StructureToPtr(struRuleAlarmInfo.struRuleInfo.uEventParam, ptrEnterInfo, false);
                    var m_struVcaArea = (CHCNetSDK.NET_VCA_AREA)Marshal.PtrToStructure(ptrEnterInfo, typeof(CHCNetSDK.NET_VCA_AREA));
                    stringAlarm = "目标进入区域，目标ID：" + struRuleAlarmInfo.struTargetInfo.dwID;
                    //m_struVcaArea.struRegion 多边形区域坐标
                    break;
                case (ushort)CHCNetSDK.VCA_RULE_EVENT_TYPE_EX.ENUM_VCA_EVENT_EXIT_AREA:
                    IntPtr ptrExitInfo = Marshal.AllocHGlobal((Int32)dwSize);
                    Marshal.StructureToPtr(struRuleAlarmInfo.struRuleInfo.uEventParam, ptrExitInfo, false);
                    m_struVcaArea = (CHCNetSDK.NET_VCA_AREA)Marshal.PtrToStructure(ptrExitInfo, typeof(CHCNetSDK.NET_VCA_AREA));
                    stringAlarm = "目标离开区域，目标ID：" + struRuleAlarmInfo.struTargetInfo.dwID;
                    //m_struVcaArea.struRegion 多边形区域坐标
                    break;
                case (ushort)CHCNetSDK.VCA_RULE_EVENT_TYPE_EX.ENUM_VCA_EVENT_INTRUSION:
                    IntPtr ptrIntrusionInfo = Marshal.AllocHGlobal((Int32)dwSize);
                    Marshal.StructureToPtr(struRuleAlarmInfo.struRuleInfo.uEventParam, ptrIntrusionInfo, false);
                    var m_struIntrusion = (CHCNetSDK.NET_VCA_INTRUSION)Marshal.PtrToStructure(ptrIntrusionInfo, typeof(CHCNetSDK.NET_VCA_INTRUSION));

                    int i = 0;
                    string strRegion = "";
                    for (i = 0; i < m_struIntrusion.struRegion.dwPointNum; i++)
                    {
                        strRegion = strRegion + "(" + m_struIntrusion.struRegion.struPos[i].fX + "," + m_struIntrusion.struRegion.struPos[i].fY + ")";
                    }
                    stringAlarm = "周界入侵，目标ID：" + struRuleAlarmInfo.struTargetInfo.dwID + "，区域范围：" + strRegion;
                    //m_struIntrusion.struRegion 多边形区域坐标
                    break;
                default:
                    stringAlarm = "其他行为分析报警，目标ID：" + struRuleAlarmInfo.struTargetInfo.dwID;
                    break;
            }


            //报警图片保存
            if (struRuleAlarmInfo.dwPicDataLen > 0)
            {
                string str = ".\\picture\\UserID_" + pAlarmer.lUserID + "_行为分析_" + iFileNumber + ".jpg";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struRuleAlarmInfo.dwPicDataLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struRuleAlarmInfo.pImage, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }

            //报警时间：年月日时分秒
            string strTimeYear = ((struRuleAlarmInfo.dwAbsTime >> 26) + 2000).ToString();
            string strTimeMonth = ((struRuleAlarmInfo.dwAbsTime >> 22) & 15).ToString("d2");
            string strTimeDay = ((struRuleAlarmInfo.dwAbsTime >> 17) & 31).ToString("d2");
            string strTimeHour = ((struRuleAlarmInfo.dwAbsTime >> 12) & 31).ToString("d2");
            string strTimeMinute = ((struRuleAlarmInfo.dwAbsTime >> 6) & 63).ToString("d2");
            string strTimeSecond = ((struRuleAlarmInfo.dwAbsTime >> 0) & 63).ToString("d2");
            string strTime = strTimeYear + "-" + strTimeMonth + "-" + strTimeDay + " " + strTimeHour + ":" + strTimeMinute + ":" + strTimeSecond;

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(struRuleAlarmInfo.struDevInfo.struDevIP.sIpV4).TrimEnd('\0');

            
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(strTime, strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_Plate(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_PLATE_RESULT struPlateResultInfo = new CHCNetSDK.NET_DVR_PLATE_RESULT();
            uint dwSize = (uint)Marshal.SizeOf(struPlateResultInfo);

            struPlateResultInfo = (CHCNetSDK.NET_DVR_PLATE_RESULT)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_PLATE_RESULT));

            //保存抓拍图片
            string str = "";
            if (struPlateResultInfo.byResultType == 1 && struPlateResultInfo.dwPicLen != 0)
            {
                str = ".\\picture\\Plate_UserID_" + pAlarmer.lUserID + "_近景图_" + iFileNumber + ".jpg";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struPlateResultInfo.dwPicLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struPlateResultInfo.pBuffer1, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }
            if (struPlateResultInfo.dwPicPlateLen != 0)
            {
                str = ".\\picture\\Plate_UserID_" + pAlarmer.lUserID + "_车牌图_" + iFileNumber + ".jpg";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struPlateResultInfo.dwPicPlateLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struPlateResultInfo.pBuffer2, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }
            if (struPlateResultInfo.dwFarCarPicLen != 0)
            {
                str = ".\\picture\\Plate_UserID_" + pAlarmer.lUserID + "_远景图_" + iFileNumber + ".jpg";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struPlateResultInfo.dwFarCarPicLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struPlateResultInfo.pBuffer5, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //抓拍时间：年月日时分秒
            string strTimeYear = System.Text.Encoding.UTF8.GetString(struPlateResultInfo.byAbsTime).TrimEnd('\0');

            //上传结果
            string stringPlateLicense = System.Text.Encoding.GetEncoding("GBK").GetString(struPlateResultInfo.struPlateInfo.sLicense).TrimEnd('\0');
            string stringAlarm = "抓拍上传，" + "车牌：" + stringPlateLicense + "，车辆序号：" + struPlateResultInfo.struVehicleInfo.dwIndex;

           
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_ITSPlate(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_ITS_PLATE_RESULT struITSPlateResult = new CHCNetSDK.NET_ITS_PLATE_RESULT();
            uint dwSize = (uint)Marshal.SizeOf(struITSPlateResult);

            struITSPlateResult = (CHCNetSDK.NET_ITS_PLATE_RESULT)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_ITS_PLATE_RESULT));

            //保存抓拍图片
            for (int i = 0; i < struITSPlateResult.dwPicNum; i++)
            {
                if (struITSPlateResult.struPicInfo[i].dwDataLen != 0)
                {
                    string str = ".\\picture\\ITS_UserID_[" + pAlarmer.lUserID + "]_Pictype_" + struITSPlateResult.struPicInfo[i].byType
                        + "_PicNum[" + (i + 1) + "]_" + iFileNumber + ".jpg";
                    FileStream fs = new FileStream(str, FileMode.Create);
                    int iLen = (int)struITSPlateResult.struPicInfo[i].dwDataLen;
                    byte[] by = new byte[iLen];
                    Marshal.Copy(struITSPlateResult.struPicInfo[i].pBuffer, by, 0, iLen);
                    fs.Write(by, 0, iLen);
                    fs.Close();
                    iFileNumber++;
                }
            }
            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //抓拍时间：年月日时分秒
            string strTimeYear = string.Format("{0:D4}", struITSPlateResult.struSnapFirstPicTime.wYear) +
                string.Format("{0:D2}", struITSPlateResult.struSnapFirstPicTime.byMonth) +
                string.Format("{0:D2}", struITSPlateResult.struSnapFirstPicTime.byDay) + " "
                + string.Format("{0:D2}", struITSPlateResult.struSnapFirstPicTime.byHour) + ":"
                + string.Format("{0:D2}", struITSPlateResult.struSnapFirstPicTime.byMinute) + ":"
                + string.Format("{0:D2}", struITSPlateResult.struSnapFirstPicTime.bySecond) + ":"
                + string.Format("{0:D3}", struITSPlateResult.struSnapFirstPicTime.wMilliSec);

            //上传结果
            string stringPlateLicense = System.Text.Encoding.GetEncoding("GBK").GetString(struITSPlateResult.struPlateInfo.sLicense).TrimEnd('\0');
            string stringAlarm = "抓拍上传，" + "车牌：" + stringPlateLicense + "，车辆序号：" + struITSPlateResult.struVehicleInfo.dwIndex;

                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }
        private static void ProcessCommAlarm_TPSRealInfo(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_TPS_REAL_TIME_INFO struTPSInfo = new CHCNetSDK.NET_DVR_TPS_REAL_TIME_INFO();
            uint dwSize = (uint)Marshal.SizeOf(struTPSInfo);

            struTPSInfo = (CHCNetSDK.NET_DVR_TPS_REAL_TIME_INFO)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_TPS_REAL_TIME_INFO));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //抓拍时间：年月日时分秒
            string strTimeYear = string.Format("{0:D4}", struTPSInfo.struTime.wYear) +
                string.Format("{0:D2}", struTPSInfo.struTime.byMonth) +
                string.Format("{0:D2}", struTPSInfo.struTime.byDay) + " "
                + string.Format("{0:D2}", struTPSInfo.struTime.byHour) + ":"
                + string.Format("{0:D2}", struTPSInfo.struTime.byMinute) + ":"
                + string.Format("{0:D2}", struTPSInfo.struTime.bySecond) + ":"
                + string.Format("{0:D3}", struTPSInfo.struTime.wMilliSec);

            //上传结果
            string stringAlarm = "TPS实时过车数据，" + "通道号：" + struTPSInfo.dwChan +
                "，设备ID：" + struTPSInfo.struTPSRealTimeInfo.wDeviceID +
                "，开始码：" + struTPSInfo.struTPSRealTimeInfo.byStart +
                "，命令号：" + struTPSInfo.struTPSRealTimeInfo.byCMD +
                "，对应车道：" + struTPSInfo.struTPSRealTimeInfo.byLane +
                "，对应车速：" + struTPSInfo.struTPSRealTimeInfo.bySpeed +
                "，byLaneState：" + struTPSInfo.struTPSRealTimeInfo.byLaneState +
                "，byQueueLen：" + struTPSInfo.struTPSRealTimeInfo.byQueueLen +
                "，wLoopState：" + struTPSInfo.struTPSRealTimeInfo.wLoopState +
                "，wStateMask：" + struTPSInfo.struTPSRealTimeInfo.wStateMask +
                "，dwDownwardFlow：" + struTPSInfo.struTPSRealTimeInfo.dwDownwardFlow +
                "，dwUpwardFlow：" + struTPSInfo.struTPSRealTimeInfo.dwUpwardFlow +
                "，byJamLevel：" + struTPSInfo.struTPSRealTimeInfo.byJamLevel;

                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_TPSStatInfo(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_TPS_STATISTICS_INFO struTPSStatInfo = new CHCNetSDK.NET_DVR_TPS_STATISTICS_INFO();
            uint dwSize = (uint)Marshal.SizeOf(struTPSStatInfo);

            struTPSStatInfo = (CHCNetSDK.NET_DVR_TPS_STATISTICS_INFO)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_TPS_STATISTICS_INFO));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //抓拍时间：年月日时分秒
            string strTimeYear = string.Format("{0:D4}", struTPSStatInfo.struTPSStatisticsInfo.struStartTime.wYear) +
                string.Format("{0:D2}", struTPSStatInfo.struTPSStatisticsInfo.struStartTime.byMonth) +
                string.Format("{0:D2}", struTPSStatInfo.struTPSStatisticsInfo.struStartTime.byDay) + " "
                + string.Format("{0:D2}", struTPSStatInfo.struTPSStatisticsInfo.struStartTime.byHour) + ":"
                + string.Format("{0:D2}", struTPSStatInfo.struTPSStatisticsInfo.struStartTime.byMinute) + ":"
                + string.Format("{0:D2}", struTPSStatInfo.struTPSStatisticsInfo.struStartTime.bySecond) + ":"
                + string.Format("{0:D3}", struTPSStatInfo.struTPSStatisticsInfo.struStartTime.wMilliSec);

            //上传结果
            string stringAlarm = "TPS统计过车数据，" + "通道号：" + struTPSStatInfo.dwChan +
                "，开始码：" + struTPSStatInfo.struTPSStatisticsInfo.byStart +
                "，命令号：" + struTPSStatInfo.struTPSStatisticsInfo.byCMD +
                "，统计开始时间：" + strTimeYear +
                "，统计时间(秒)：" + struTPSStatInfo.struTPSStatisticsInfo.dwSamplePeriod;


            for (int i = 0; i < CHCNetSDK.MAX_TPS_RULE; i++)
            {
                stringAlarm = stringAlarm + "车道号: " + struTPSStatInfo.struTPSStatisticsInfo.struLaneParam[i].byLane +
                    "，车道过车平均速度:" + struTPSStatInfo.struTPSStatisticsInfo.struLaneParam[i].bySpeed +
                    "，小型车数量:" + struTPSStatInfo.struTPSStatisticsInfo.struLaneParam[i].dwLightVehicle +
                    "，中型车数量:" + struTPSStatInfo.struTPSStatisticsInfo.struLaneParam[i].dwMidVehicle +
                    "，重型车数量:" + struTPSStatInfo.struTPSStatisticsInfo.struLaneParam[i].dwHeavyVehicle +
                    "，车头时距:" + struTPSStatInfo.struTPSStatisticsInfo.struLaneParam[i].dwTimeHeadway +
                    "，车头间距:" + struTPSStatInfo.struTPSStatisticsInfo.struLaneParam[i].dwSpaceHeadway +
                    "，空间占有率:" + struTPSStatInfo.struTPSStatisticsInfo.struLaneParam[i].fSpaceOccupyRation +
                    "，时间占有率:" + struTPSStatInfo.struTPSStatisticsInfo.struLaneParam[i].fTimeOccupyRation;
            }

           
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_PDC(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_PDC_ALRAM_INFO struPDCInfo = new CHCNetSDK.NET_DVR_PDC_ALRAM_INFO();
            uint dwSize = (uint)Marshal.SizeOf(struPDCInfo);
            struPDCInfo = (CHCNetSDK.NET_DVR_PDC_ALRAM_INFO)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_PDC_ALRAM_INFO));

            string stringAlarm = "客流量统计，进入人数：" + struPDCInfo.dwEnterNum + "，离开人数：" + struPDCInfo.dwLeaveNum;

            uint dwUnionSize = (uint)Marshal.SizeOf(struPDCInfo.uStatModeParam);
            IntPtr ptrPDCUnion = Marshal.AllocHGlobal((Int32)dwUnionSize);
            Marshal.StructureToPtr(struPDCInfo.uStatModeParam, ptrPDCUnion, false);

            if (struPDCInfo.byMode == 0) //单帧统计结果，此处为UTC时间
            {
                var m_struStatFrame = (CHCNetSDK.UNION_STATFRAME)Marshal.PtrToStructure(ptrPDCUnion, typeof(CHCNetSDK.UNION_STATFRAME));
                stringAlarm = stringAlarm + "，单帧统计，相对时标：" + m_struStatFrame.dwRelativeTime + "，绝对时标：" + m_struStatFrame.dwAbsTime;
            }
            if (struPDCInfo.byMode == 1) //最小时间段统计结果
            {
               var m_struStatTime = (CHCNetSDK.UNION_STATTIME)Marshal.PtrToStructure(ptrPDCUnion, typeof(CHCNetSDK.UNION_STATTIME));

                //开始时间
                string strStartTime = string.Format("{0:D4}", m_struStatTime.tmStart.dwYear) +
                string.Format("{0:D2}", m_struStatTime.tmStart.dwMonth) +
                string.Format("{0:D2}", m_struStatTime.tmStart.dwDay) + " "
                + string.Format("{0:D2}", m_struStatTime.tmStart.dwHour) + ":"
                + string.Format("{0:D2}", m_struStatTime.tmStart.dwMinute) + ":"
                + string.Format("{0:D2}", m_struStatTime.tmStart.dwSecond);

                //结束时间
                string strEndTime = string.Format("{0:D4}", m_struStatTime.tmEnd.dwYear) +
                string.Format("{0:D2}", m_struStatTime.tmEnd.dwMonth) +
                string.Format("{0:D2}", m_struStatTime.tmEnd.dwDay) + " "
                + string.Format("{0:D2}", m_struStatTime.tmEnd.dwHour) + ":"
                + string.Format("{0:D2}", m_struStatTime.tmEnd.dwMinute) + ":"
                + string.Format("{0:D2}", m_struStatTime.tmEnd.dwSecond);

                stringAlarm = stringAlarm + "，最小时间段统计，开始时间：" + strStartTime + "，结束时间：" + strEndTime;
            }
            Marshal.FreeHGlobal(ptrPDCUnion);

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');


            
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }


        private static void ProcessCommAlarm_PARK(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_ITS_PARK_VEHICLE struParkInfo = new CHCNetSDK.NET_ITS_PARK_VEHICLE();
            uint dwSize = (uint)Marshal.SizeOf(struParkInfo);
            struParkInfo = (CHCNetSDK.NET_ITS_PARK_VEHICLE)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_ITS_PARK_VEHICLE));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //保存抓拍图片
            for (int i = 0; i < struParkInfo.dwPicNum; i++)
            {
                if ((struParkInfo.struPicInfo[i].dwDataLen != 0) && (struParkInfo.struPicInfo[i].pBuffer != IntPtr.Zero))
                {
                    string str = ".\\picture\\Device_Park_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_Pictype_" + struParkInfo.struPicInfo[i].byType
                        + "_PicNum[" + (i + 1) + "]_" + iFileNumber + ".jpg";
                    FileStream fs = new FileStream(str, FileMode.Create);
                    int iLen = (int)struParkInfo.struPicInfo[i].dwDataLen;
                    byte[] by = new byte[iLen];
                    Marshal.Copy(struParkInfo.struPicInfo[i].pBuffer, by, 0, iLen);
                    fs.Write(by, 0, iLen);
                    fs.Close();
                    iFileNumber++;
                }
            }

            string stringAlarm = "停车场数据上传，异常状态：" + struParkInfo.byParkError + "，车位编号：" + struParkInfo.byParkingNo +
                ", 车辆状态：" + struParkInfo.byLocationStatus + "，车牌号码：" +
                System.Text.Encoding.GetEncoding("GBK").GetString(struParkInfo.struPlateInfo.sLicense).TrimEnd('\0');

            
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_VQD(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_DIAGNOSIS_UPLOAD struVQDInfo = new CHCNetSDK.NET_DVR_DIAGNOSIS_UPLOAD();
            uint dwSize = (uint)Marshal.SizeOf(struVQDInfo);
            struVQDInfo = (CHCNetSDK.NET_DVR_DIAGNOSIS_UPLOAD)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_DIAGNOSIS_UPLOAD));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //开始时间
            string strCheckTime = string.Format("{0:D4}", struVQDInfo.struCheckTime.dwYear) +
            string.Format("{0:D2}", struVQDInfo.struCheckTime.dwMonth) +
            string.Format("{0:D2}", struVQDInfo.struCheckTime.dwDay) + " "
            + string.Format("{0:D2}", struVQDInfo.struCheckTime.dwHour) + ":"
            + string.Format("{0:D2}", struVQDInfo.struCheckTime.dwMinute) + ":"
            + string.Format("{0:D2}", struVQDInfo.struCheckTime.dwSecond);

            string stringAlarm = "视频质量诊断结果，流ID：" + struVQDInfo.sStreamID + "，监测点IP：" + struVQDInfo.sMonitorIP + "，监控点通道号：" + struVQDInfo.dwChanIndex +
                "，检测时间：" + strCheckTime + "，byResult：" + struVQDInfo.byResult + "，bySignalResult：" + struVQDInfo.bySignalResult + "，byBlurResult：" + struVQDInfo.byBlurResult;

            
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_FaceSnap(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_VCA_FACESNAP_RESULT struFaceSnapInfo = new CHCNetSDK.NET_VCA_FACESNAP_RESULT();
            uint dwSize = (uint)Marshal.SizeOf(struFaceSnapInfo);
            struFaceSnapInfo = (CHCNetSDK.NET_VCA_FACESNAP_RESULT)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_VCA_FACESNAP_RESULT));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //保存抓拍图片数据
            if ((struFaceSnapInfo.dwBackgroundPicLen != 0) && (struFaceSnapInfo.pBuffer2 != IntPtr.Zero))
            {
                string str = ".\\picture\\FaceSnap_CapPic_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_" + iFileNumber + ".jpg";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struFaceSnapInfo.dwBackgroundPicLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struFaceSnapInfo.pBuffer2, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }

            //报警时间：年月日时分秒
            string strTimeYear = ((struFaceSnapInfo.dwAbsTime >> 26) + 2000).ToString();
            string strTimeMonth = ((struFaceSnapInfo.dwAbsTime >> 22) & 15).ToString("d2");
            string strTimeDay = ((struFaceSnapInfo.dwAbsTime >> 17) & 31).ToString("d2");
            string strTimeHour = ((struFaceSnapInfo.dwAbsTime >> 12) & 31).ToString("d2");
            string strTimeMinute = ((struFaceSnapInfo.dwAbsTime >> 6) & 63).ToString("d2");
            string strTimeSecond = ((struFaceSnapInfo.dwAbsTime >> 0) & 63).ToString("d2");
            string strTime = strTimeYear + "-" + strTimeMonth + "-" + strTimeDay + " " + strTimeHour + ":" + strTimeMinute + ":" + strTimeSecond;

            string stringAlarm = "人脸抓拍结果，前端设备：" + System.Text.Encoding.UTF8.GetString(struFaceSnapInfo.struDevInfo.struDevIP.sIpV4).TrimEnd('\0') +
                "，通道号：" + struFaceSnapInfo.struDevInfo.byIvmsChannel + "，报警时间：" + strTime;

                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_FaceMatch(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_VCA_FACESNAP_MATCH_ALARM struFaceMatchAlarm = new CHCNetSDK.NET_VCA_FACESNAP_MATCH_ALARM();
            uint dwSize = (uint)Marshal.SizeOf(struFaceMatchAlarm);
            struFaceMatchAlarm = (CHCNetSDK.NET_VCA_FACESNAP_MATCH_ALARM)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_VCA_FACESNAP_MATCH_ALARM));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //保存抓拍人脸子图图片数据
            if ((struFaceMatchAlarm.struSnapInfo.dwSnapFacePicLen != 0) && (struFaceMatchAlarm.struSnapInfo.pBuffer1 != IntPtr.Zero))
            {
                string str = ".\\picture\\FaceMatch_FacePic_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_" + iFileNumber + ".jpg";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struFaceMatchAlarm.struSnapInfo.dwSnapFacePicLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struFaceMatchAlarm.struSnapInfo.pBuffer1, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }

            //保存比对结果人脸库人脸图片数据
            if ((struFaceMatchAlarm.struBlackListInfo.dwBlackListPicLen != 0) && (struFaceMatchAlarm.struBlackListInfo.pBuffer1 != IntPtr.Zero))
            {
                string str = ".\\picture\\FaceMatch_BlackListPic_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]" +
                    "_fSimilarity[" + struFaceMatchAlarm.fSimilarity + "]_" + iFileNumber + ".jpg";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struFaceMatchAlarm.struBlackListInfo.dwBlackListPicLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struFaceMatchAlarm.struBlackListInfo.pBuffer1, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }

            //抓拍时间：年月日时分秒
            string strTimeYear = ((struFaceMatchAlarm.struSnapInfo.dwAbsTime >> 26) + 2000).ToString();
            string strTimeMonth = ((struFaceMatchAlarm.struSnapInfo.dwAbsTime >> 22) & 15).ToString("d2");
            string strTimeDay = ((struFaceMatchAlarm.struSnapInfo.dwAbsTime >> 17) & 31).ToString("d2");
            string strTimeHour = ((struFaceMatchAlarm.struSnapInfo.dwAbsTime >> 12) & 31).ToString("d2");
            string strTimeMinute = ((struFaceMatchAlarm.struSnapInfo.dwAbsTime >> 6) & 63).ToString("d2");
            string strTimeSecond = ((struFaceMatchAlarm.struSnapInfo.dwAbsTime >> 0) & 63).ToString("d2");
            string strTime = strTimeYear + "-" + strTimeMonth + "-" + strTimeDay + " " + strTimeHour + ":" + strTimeMinute + ":" + strTimeSecond;

            string stringAlarm = "人脸比对报警，抓拍设备：" + System.Text.Encoding.UTF8.GetString(struFaceMatchAlarm.struSnapInfo.struDevInfo.struDevIP.sIpV4).TrimEnd('\0') + "，抓拍时间："
                + strTime + "，相似度：" + struFaceMatchAlarm.fSimilarity;

            
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_FaceDetect(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_FACE_DETECTION struFaceDetectInfo = new CHCNetSDK.NET_DVR_FACE_DETECTION();
            uint dwSize = (uint)Marshal.SizeOf(struFaceDetectInfo);
            struFaceDetectInfo = (CHCNetSDK.NET_DVR_FACE_DETECTION)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_FACE_DETECTION));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0').TrimEnd('\0');

            //报警时间：年月日时分秒
            string strTimeYear = ((struFaceDetectInfo.dwAbsTime >> 26) + 2000).ToString();
            string strTimeMonth = ((struFaceDetectInfo.dwAbsTime >> 22) & 15).ToString("d2");
            string strTimeDay = ((struFaceDetectInfo.dwAbsTime >> 17) & 31).ToString("d2");
            string strTimeHour = ((struFaceDetectInfo.dwAbsTime >> 12) & 31).ToString("d2");
            string strTimeMinute = ((struFaceDetectInfo.dwAbsTime >> 6) & 63).ToString("d2");
            string strTimeSecond = ((struFaceDetectInfo.dwAbsTime >> 0) & 63).ToString("d2");
            string strTime = strTimeYear + "-" + strTimeMonth + "-" + strTimeDay + " " + strTimeHour + ":" + strTimeMinute + ":" + strTimeSecond;

            string stringAlarm = "人脸抓拍结果结果，前端设备：" + System.Text.Encoding.UTF8.GetString(struFaceDetectInfo.struDevInfo.struDevIP.sIpV4) + "，报警时间：" + strTime;

            //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_CIDAlarm(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_CID_ALARM struCIDAlarm = new CHCNetSDK.NET_DVR_CID_ALARM();
            uint dwSize = (uint)Marshal.SizeOf(struCIDAlarm);
            struCIDAlarm = (CHCNetSDK.NET_DVR_CID_ALARM)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_CID_ALARM));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //报警时间：年月日时分秒
            string strTimeYear = (struCIDAlarm.struTriggerTime.wYear).ToString();
            string strTimeMonth = (struCIDAlarm.struTriggerTime.byMonth).ToString("d2");
            string strTimeDay = (struCIDAlarm.struTriggerTime.byDay).ToString("d2");
            string strTimeHour = (struCIDAlarm.struTriggerTime.byHour).ToString("d2");
            string strTimeMinute = (struCIDAlarm.struTriggerTime.byMinute).ToString("d2");
            string strTimeSecond = (struCIDAlarm.struTriggerTime.bySecond).ToString("d2");
            string strTime = strTimeYear + "-" + strTimeMonth + "-" + strTimeDay + " " + strTimeHour + ":" + strTimeMinute + ":" + strTimeSecond;

            string stringAlarm = "报警主机CID报告，sCIDCode：" + System.Text.Encoding.UTF8.GetString(struCIDAlarm.sCIDCode).TrimEnd('\0')
                + "，sCIDDescribe：" + System.Text.Encoding.UTF8.GetString(struCIDAlarm.sCIDDescribe).TrimEnd('\0')
                + "，报告类型：" + struCIDAlarm.byReportType + "，防区号：" + struCIDAlarm.wDefenceNo + "，报警触发时间：" + strTime;

            
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }

        private static void ProcessCommAlarm_InterComEvent(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_VIDEO_INTERCOM_EVENT struInterComEvent = new CHCNetSDK.NET_DVR_VIDEO_INTERCOM_EVENT();
            uint dwSize = (uint)Marshal.SizeOf(struInterComEvent);
            struInterComEvent = (CHCNetSDK.NET_DVR_VIDEO_INTERCOM_EVENT)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_VIDEO_INTERCOM_EVENT));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            if (struInterComEvent.byEventType == 3)
            {
                CHCNetSDK.NET_DVR_AUTH_INFO struAuthInfo = new CHCNetSDK.NET_DVR_AUTH_INFO();
                int dwUnionSize = Marshal.SizeOf(struInterComEvent.uEventInfo);
                IntPtr ptrAuthInfo = Marshal.AllocHGlobal(dwUnionSize);
                Marshal.StructureToPtr(struInterComEvent.uEventInfo, ptrAuthInfo, false);
                struAuthInfo = (CHCNetSDK.NET_DVR_AUTH_INFO)Marshal.PtrToStructure(ptrAuthInfo, typeof(CHCNetSDK.NET_DVR_AUTH_INFO));
                Marshal.FreeHGlobal(ptrAuthInfo);

                //保存抓拍图片
                if ((struAuthInfo.dwPicDataLen != 0) && (struAuthInfo.pImage != IntPtr.Zero))
                {
                    string str = ".\\picture\\Device_InterCom_CapturePic_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_" + iFileNumber + ".jpg";
                    FileStream fs = new FileStream(str, FileMode.Create);
                    int iLen = (int)struAuthInfo.dwPicDataLen;
                    byte[] by = new byte[iLen];
                    Marshal.Copy(struAuthInfo.pImage, by, 0, iLen);
                    fs.Write(by, 0, iLen);
                    fs.Close();
                    iFileNumber++;
                }
            }

            //报警时间：年月日时分秒
            string strTimeYear = (struInterComEvent.struTime.wYear).ToString();
            string strTimeMonth = (struInterComEvent.struTime.byMonth).ToString("d2");
            string strTimeDay = (struInterComEvent.struTime.byDay).ToString("d2");
            string strTimeHour = (struInterComEvent.struTime.byHour).ToString("d2");
            string strTimeMinute = (struInterComEvent.struTime.byMinute).ToString("d2");
            string strTimeSecond = (struInterComEvent.struTime.bySecond).ToString("d2");
            string strTime = strTimeYear + "-" + strTimeMonth + "-" + strTimeDay + " " + strTimeHour + ":" + strTimeMinute + ":" + strTimeSecond;

            string stringAlarm = "可视对讲事件，byEventType：" + struInterComEvent.byEventType + "，设备编号："
                + System.Text.Encoding.UTF8.GetString(struInterComEvent.byDevNumber).TrimEnd('\0') + "，报警触发时间：" + strTime;
            
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);

        }

        private static void ProcessCommAlarm_AcsAlarm(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_ACS_ALARM_INFO struAcsAlarm = new CHCNetSDK.NET_DVR_ACS_ALARM_INFO();
            uint dwSize = (uint)Marshal.SizeOf(struAcsAlarm);
            struAcsAlarm = (CHCNetSDK.NET_DVR_ACS_ALARM_INFO)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_ACS_ALARM_INFO));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //保存抓拍图片
            if ((struAcsAlarm.dwPicDataLen != 0) && (struAcsAlarm.pPicData != IntPtr.Zero))
            {
                string str = ".\\picture\\Device_Acs_CapturePic_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_" + iFileNumber + ".jpg";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struAcsAlarm.dwPicDataLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struAcsAlarm.pPicData, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }

            //报警时间：年月日时分秒
            string strTimeYear = (struAcsAlarm.struTime.dwYear).ToString();
            string strTimeMonth = (struAcsAlarm.struTime.dwMonth).ToString("d2");
            string strTimeDay = (struAcsAlarm.struTime.dwDay).ToString("d2");
            string strTimeHour = (struAcsAlarm.struTime.dwHour).ToString("d2");
            string strTimeMinute = (struAcsAlarm.struTime.dwMinute).ToString("d2");
            string strTimeSecond = (struAcsAlarm.struTime.dwSecond).ToString("d2");
            string strTime = strTimeYear + "-" + strTimeMonth + "-" + strTimeDay + " " + strTimeHour + ":" + strTimeMinute + ":" + strTimeSecond;

            string stringAlarm = "门禁主机报警信息，dwMajor：" + struAcsAlarm.dwMajor + "，dwMinor：" + struAcsAlarm.dwMinor + "，卡号："
                + System.Text.Encoding.UTF8.GetString(struAcsAlarm.struAcsEventInfo.byCardNo).TrimEnd('\0') + "，读卡器编号：" +
                struAcsAlarm.struAcsEventInfo.dwCardReaderNo + "，报警触发时间：" + strTime;

             //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);

        }

        private static void ProcessCommAlarm_IDInfoAlarm(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_ID_CARD_INFO_ALARM struIDInfoAlarm = new CHCNetSDK.NET_DVR_ID_CARD_INFO_ALARM();
            uint dwSize = (uint)Marshal.SizeOf(struIDInfoAlarm);
            struIDInfoAlarm = (CHCNetSDK.NET_DVR_ID_CARD_INFO_ALARM)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_ID_CARD_INFO_ALARM));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //保存抓拍图片
            if ((struIDInfoAlarm.dwCapturePicDataLen != 0) && (struIDInfoAlarm.pCapturePicData != IntPtr.Zero))
            {
                string str = ".\\picture\\Device_ID_CapturePic_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_" + iFileNumber + ".jpg";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struIDInfoAlarm.dwCapturePicDataLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struIDInfoAlarm.pCapturePicData, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }

            //保存身份证图片数据
            if ((struIDInfoAlarm.dwPicDataLen != 0) && (struIDInfoAlarm.pPicData != IntPtr.Zero))
            {
                string str = ".\\picture\\Device_ID_IDPic_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_" + iFileNumber + ".jpg";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struIDInfoAlarm.dwPicDataLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struIDInfoAlarm.pPicData, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }

            //保存指纹数据
            if ((struIDInfoAlarm.dwFingerPrintDataLen != 0) && (struIDInfoAlarm.pFingerPrintData != IntPtr.Zero))
            {
                string str = ".\\picture\\Device_ID_FingerPrint_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_" + iFileNumber + ".data";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struIDInfoAlarm.dwFingerPrintDataLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struIDInfoAlarm.pFingerPrintData, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }

            //报警时间：年月日时分秒
            string strTimeYear = (struIDInfoAlarm.struSwipeTime.wYear).ToString();
            string strTimeMonth = (struIDInfoAlarm.struSwipeTime.byMonth).ToString("d2");
            string strTimeDay = (struIDInfoAlarm.struSwipeTime.byDay).ToString("d2");
            string strTimeHour = (struIDInfoAlarm.struSwipeTime.byHour).ToString("d2");
            string strTimeMinute = (struIDInfoAlarm.struSwipeTime.byMinute).ToString("d2");
            string strTimeSecond = (struIDInfoAlarm.struSwipeTime.bySecond).ToString("d2");
            string strTime = strTimeYear + "-" + strTimeMonth + "-" + strTimeDay + " " + strTimeHour + ":" + strTimeMinute + ":" + strTimeSecond;

            string stringAlarm = "身份证刷卡信息，dwMajor：" + struIDInfoAlarm.dwMajor + "，dwMinor：" + struIDInfoAlarm.dwMinor
                + "，身份证号：" + System.Text.Encoding.UTF8.GetString(struIDInfoAlarm.struIDCardCfg.byIDNum).TrimEnd('\0') +
                "，姓名：" + System.Text.Encoding.UTF8.GetString(struIDInfoAlarm.struIDCardCfg.byName).TrimEnd('\0') +
                "，刷卡时间：" + strTime;

            //创建该控件的主线程直接更新信息列表 
            UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);

        }

        private static void ProcessCommAlarm_AIOPVideo(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_AIOP_VIDEO_HEAD struAIOPVideo = new CHCNetSDK.NET_AIOP_VIDEO_HEAD();
            uint dwSize = (uint)Marshal.SizeOf(struAIOPVideo);
            struAIOPVideo = (CHCNetSDK.NET_AIOP_VIDEO_HEAD)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_AIOP_VIDEO_HEAD));

            //报警设备struAIOPPic地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //报警时间：年月日时分秒
            string strTimeYear = (struAIOPVideo.struTime.wYear).ToString();
            string strTimeMonth = (struAIOPVideo.struTime.wMonth).ToString("d2");
            string strTimeDay = (struAIOPVideo.struTime.wDay).ToString("d2");
            string strTimeHour = (struAIOPVideo.struTime.wHour).ToString("d2");
            string strTimeMinute = (struAIOPVideo.struTime.wMinute).ToString("d2");
            string strTimeSecond = (struAIOPVideo.struTime.wSecond).ToString("d2");
            string strTime = strTimeYear + "-" + strTimeMonth + "-" + strTimeDay + " " + strTimeHour + ":" + strTimeMinute + ":" + strTimeSecond;

            string stringAlarm = "AI开放平台视频检测报警上传，szTaskID：" + System.Text.Encoding.UTF8.GetString(struAIOPVideo.szTaskID).TrimEnd('\0')
                + ",报警触发时间：" + strTime;

            //保存AIOPData数据  
            if ((struAIOPVideo.dwAIOPDataSize != 0) && (struAIOPVideo.pBufferAIOPData != IntPtr.Zero))
            {
                string str = ".\\picture\\AiopData[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]" +
                     iFileNumber + ".txt";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struAIOPVideo.dwAIOPDataSize;
                byte[] by = new byte[iLen];
                Marshal.Copy(struAIOPVideo.pBufferAIOPData, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }
            //保存图片数据
            if ((struAIOPVideo.dwPictureSize != 0) && (struAIOPVideo.pBufferPicture != IntPtr.Zero))
            {
                string strPic = ".\\picture\\AiopPicture[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]" +
                     iFileNumber + ".jpg";
                FileStream fsPic = new FileStream(strPic, FileMode.Create);
                int iPicLen = (int)struAIOPVideo.dwPictureSize;
                byte[] byPic = new byte[iPicLen];
                Marshal.Copy(struAIOPVideo.pBufferPicture, byPic, 0, iPicLen);
                fsPic.Write(byPic, 0, iPicLen);
                fsPic.Close();
                iFileNumber++;
            }
            
                //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
        }
        private static void ProcessCommAlarm_AIOPPicture(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_AIOP_PICTURE_HEAD struAIOPPic = new CHCNetSDK.NET_AIOP_PICTURE_HEAD();
            uint dwSize = (uint)Marshal.SizeOf(struAIOPPic);
            struAIOPPic = (CHCNetSDK.NET_AIOP_PICTURE_HEAD)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_AIOP_PICTURE_HEAD));

            //报警设备struAIOPPic地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //报警时间：年月日时分秒
            string strTimeYear = (struAIOPPic.struTime.wYear).ToString();
            string strTimeMonth = (struAIOPPic.struTime.wMonth).ToString("d2");
            string strTimeDay = (struAIOPPic.struTime.wDay).ToString("d2");
            string strTimeHour = (struAIOPPic.struTime.wHour).ToString("d2");
            string strTimeMinute = (struAIOPPic.struTime.wMinute).ToString("d2");
            string strTimeSecond = (struAIOPPic.struTime.wSecond).ToString("d2");
            string strTime = strTimeYear + "-" + strTimeMonth + "-" + strTimeDay + " " + strTimeHour + ":" + strTimeMinute + ":" + strTimeSecond;

            string stringAlarm = "AI开放平台图片检测报警上传，szPID：" + System.Text.Encoding.UTF8.GetString(struAIOPPic.szPID).TrimEnd('\0')
                + ",报警触发时间：" + strTime;

            //保存AIOPData数据  
            if ((struAIOPPic.dwAIOPDataSize != 0) && (struAIOPPic.pBufferAIOPData != IntPtr.Zero))
            {
                string str = ".\\picture\\AiopData[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]" +
                     iFileNumber + ".txt";
                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struAIOPPic.dwAIOPDataSize;
                byte[] by = new byte[iLen];
                Marshal.Copy(struAIOPPic.pBufferAIOPData, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }
            //创建该控件的主线程直接更新信息列表 
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }
        private static void ProcessCommAlarm_ISAPIAlarm(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_ALARM_ISAPI_INFO struISAPIAlarm = new CHCNetSDK.NET_DVR_ALARM_ISAPI_INFO();
            uint dwSize = (uint)Marshal.SizeOf(struISAPIAlarm);
            struISAPIAlarm = (CHCNetSDK.NET_DVR_ALARM_ISAPI_INFO)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_ALARM_ISAPI_INFO));

            //报警设备IP地址
            string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');

            //保存XML或者Json数据
            string str = "";
            if ((struISAPIAlarm.dwAlarmDataLen != 0) && (struISAPIAlarm.pAlarmData != IntPtr.Zero))
            {
                if (struISAPIAlarm.byDataType == 1) // 0-invalid,1-xml,2-json
                {
                    str = ".\\picture\\ISAPI_Alarm_XmlData_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_" + iFileNumber + ".xml";
                }
                if (struISAPIAlarm.byDataType == 2) // 0-invalid,1-xml,2-json
                {
                    str = ".\\picture\\ISAPI_Alarm_JsonData_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_" + iFileNumber + ".json";
                }

                FileStream fs = new FileStream(str, FileMode.Create);
                int iLen = (int)struISAPIAlarm.dwAlarmDataLen;
                byte[] by = new byte[iLen];
                Marshal.Copy(struISAPIAlarm.pAlarmData, by, 0, iLen);
                fs.Write(by, 0, iLen);
                fs.Close();
                iFileNumber++;
            }
            for (int i = 0; i < struISAPIAlarm.byPicturesNumber; i++)
            {
                CHCNetSDK.NET_DVR_ALARM_ISAPI_PICDATA struPicData = new CHCNetSDK.NET_DVR_ALARM_ISAPI_PICDATA();
                struPicData.szFilename = new byte[256];
                Int32 nSize = Marshal.SizeOf(struPicData);
                struPicData = (CHCNetSDK.NET_DVR_ALARM_ISAPI_PICDATA)Marshal.PtrToStructure((IntPtr)((Int32)(struISAPIAlarm.pPicPackData) + i * nSize), typeof(CHCNetSDK.NET_DVR_ALARM_ISAPI_PICDATA));

                //保存图片数据
                if ((struPicData.dwPicLen != 0) && (struPicData.pPicData != IntPtr.Zero))
                {
                    str = ".\\picture\\ISAPI_Alarm_Pic_[" + strIP + "]_lUerID_[" + pAlarmer.lUserID + "]_"
                         + "_" + iFileNumber + ".jpg";

                    FileStream fs = new FileStream(str, FileMode.Create);
                    int iLen = (int)struPicData.dwPicLen;
                    byte[] by = new byte[iLen];
                    Marshal.Copy(struPicData.pPicData, by, 0, iLen);
                    fs.Write(by, 0, iLen);
                    fs.Close();
                    iFileNumber++;
                }
            }

            string stringAlarm = "ISAPI报警信息，byDataType：" + struISAPIAlarm.byDataType + "，图片张数：" + struISAPIAlarm.byPicturesNumber;
            
                UpdateClientList(DateTime.Now.ToString(), strIP, stringAlarm);
            
        }
        public static void UpdateClientList(string strAlarmTime, string strDevIP, string strAlarmMsg)
        {
            //列表新增报警信息
            //列表新增报警信息
            var alarmInfoObj = new AlarmInfo() {
                AlarmTime=strAlarmTime, DeviceIp=strDevIP, AlarmMsg=strAlarmMsg 
            };
            //listAlarmInfo.Add(alarmInfoObj);
            GetDeviceDetails gt = new GetDeviceDetails();
           gt.SaveMonitoringDetails(strDevIP, strAlarmMsg, strAlarmTime);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(alarmInfoObj));
            SendMessageToWeb(alarmInfoObj);
           // proxy.Invoke("AlarmMessage", JsonSerializer.Serialize(alarmInfoObj));
            //listViewAlarmInfo.Items.Add(new ListViewItem(new string[] { strAlarmTime, strDevIP, strAlarmMsg }));
        }

        #region Get Devices and Login

        //private void GetDevices()
        //{
        //    GetDeviceDetails getDevice = new GetDeviceDetails();
        //    var devicesList=getDevice.GetDeviceDetail();
        //    foreach(var s in devicesList)
        //    {
        //        Console.WriteLine(JsonSerializer.Serialize(s));
        //       // LoginDevices(s.DeviceIpT, s.Password, s.UserId, s.Port,s.ClassId);
        //       // LoginDevices(s.DeviceIpSt, s.Password, s.UserId, s.Port,s.ClassId);                
        //    }
        //}
        public static void cbLoginCallBack(int lUserID, int dwResult, IntPtr lpDeviceInfo, IntPtr pUser)
        {
            string strLoginCallBack = "登录设备，lUserID：" + lUserID + "，dwResult：" + dwResult;

            if (dwResult == 0)
            {
                uint iErrCode = CHCNetSDK.NET_DVR_GetLastError();
                strLoginCallBack = strLoginCallBack + "，错误号:" + iErrCode;
            }
            //创建该控件的主线程直接更新信息列表 
            UpdateClientList(strLoginCallBack, lpDeviceInfo);
            
        }
        public static void UpdateClientList(string strLogStatus, IntPtr lpDeviceInfo)
        {
            //列表新增报警信息
            //Send to Browser SignalR method
            var str = "登录状态（异步）：" + strLogStatus;
        }
        private static void LoginDevices(string ip, string password, string userid, int port, int classid, string ccmac)
        {
            Console.WriteLine(ip);
            CHCNetSDK.NET_DVR_USER_LOGIN_INFO struLogInfo = new CHCNetSDK.NET_DVR_USER_LOGIN_INFO();
            
            //设备IP地址或者域名
            byte[] byIP = System.Text.Encoding.Default.GetBytes(ip);
            struLogInfo.sDeviceAddress = new byte[129];
            byIP.CopyTo(struLogInfo.sDeviceAddress, 0);

            //设备用户名
            byte[] byUserName = System.Text.Encoding.Default.GetBytes(userid);
            struLogInfo.sUserName = new byte[64];            
            byUserName.CopyTo(struLogInfo.sUserName, 0);
            
            //设备密码
            byte[] byPassword = System.Text.Encoding.Default.GetBytes(password);
            struLogInfo.sPassword = new byte[64];
            byPassword.CopyTo(struLogInfo.sPassword, 0);
            struLogInfo.wPort = (ushort)(port);//设备服务端口号

            struLogInfo.cbLoginResult = LoginCallBack;
            struLogInfo.bUseAsynLogin = false; //是否异步登录：0- 否，1- 是
            if ((struLogInfo.bUseAsynLogin == true) && (LoginCallBack == null))
            {
                LoginCallBack = new CHCNetSDK.LOGINRESULTCALLBACK(cbLoginCallBack);//注册回调函数                    
            }

            struLogInfo.byLoginMode = 0; //0-Private, 1-ISAPI, 2-自适应
            struLogInfo.byHttps = 0; //0-不适用tls，1-使用tls 2-自适应
            CHCNetSDK.NET_DVR_DEVICEINFO_V40 DeviceInfo = new CHCNetSDK.NET_DVR_DEVICEINFO_V40();
            try
            {
                m_lUserID = CHCNetSDK.NET_DVR_Login_V40(ref struLogInfo, ref DeviceInfo);
                if (m_lUserID < 0)
                {
                    iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                    
                    strErr = "NET_DVR_Login_V30 failed, error code= " + iLastErr; //登录失败，输出错误号 Failed to login and output the error code
                   
                    Console.WriteLine(strErr);
                }
                else
                {
                    //登录成功
                    iDeviceNumber++;
                   // string str1 = "" + m_lUserID;
                    var x = new DeviceInfo() { MuserId = m_lUserID, DeviceIp = ip,
                        Message = "未布防", ClassId=classid,Ccmac=ccmac.ToUpper() };
                    deviceInfos.Add(x);//将已注册设备添加进列表
                    GetDeviceDetails gt = new GetDeviceDetails();
                    //gt.SaveCamLoginInfo(ip, classid);
                    SetAlarm(x);
                    Console.WriteLine("Successful login : "+JsonSerializer.Serialize(x));
                }
            }
            //登录设备 Login the device
           catch(Exception ex)
            {
                Console.WriteLine("error " + ex.StackTrace + " message " + ex.InnerException+
                    " message "+ ex.Message);
                Console.Read();
            }
        }
        private static  void SetAlarm(DeviceInfo device)
        {
            CHCNetSDK.NET_DVR_SETUPALARM_PARAM struAlarmParam = new CHCNetSDK.NET_DVR_SETUPALARM_PARAM();
            struAlarmParam.dwSize = (uint)Marshal.SizeOf(struAlarmParam);
            struAlarmParam.byLevel = 1; //0- 一级布防,1- 二级布防
            struAlarmParam.byAlarmInfoType = 1;//智能交通设备有效，新报警信息类型
            struAlarmParam.byFaceAlarmDetection = 1;//1-人脸侦测
            m_lUserID = device.MuserId;
            m_lAlarmHandle[m_lUserID] = CHCNetSDK.NET_DVR_SetupAlarmChan_V41(m_lUserID, ref struAlarmParam);
            if (m_lAlarmHandle[m_lUserID] < 0)
            {
                iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                strErr = "布防失败，错误号：" + iLastErr; //布防失败，输出错误号
                Console.WriteLine("error code and message: " + strErr);
            }
            else
            {
               device.Message = "布防成功";
            }
            deviceInfos.ForEach(x => Console.WriteLine(JsonSerializer.Serialize(x)));
        }
        public static void CloseAlarm(DeviceInfo device)
        {
            m_lUserID = device.MuserId;
            if (m_lAlarmHandle[m_lUserID] >= 0)
            {
                if (!CHCNetSDK.NET_DVR_CloseAlarmChan_V30(m_lAlarmHandle[m_lUserID]))
                {
                    iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                    strErr = "撤防失败，错误号：" + iLastErr; //撤防失败，输出错误号
                    Console.WriteLine(strErr);
                }
                else
                {
                    device.Message = "未布防";
                    CHCNetSDK.NET_DVR_Logout(m_lUserID);
                    deviceInfos.Remove(device);
                    m_lAlarmHandle[m_lUserID] = -1;
                }
            }
            else
            {
                device.Message = "未布防";
            }
        }
        private static void StartListenTcp()
        {
            string sLocalIP = IPAddress.Any.ToString();
            ushort wLocalPort = 7200;
            if (m_falarmData == null)
            {
                m_falarmData = new CHCNetSDK.MSGCallBack(MsgCallback);
            }

            iListenHandle = CHCNetSDK.NET_DVR_StartListen_V30(sLocalIP, wLocalPort, m_falarmData, IntPtr.Zero);
            if (iListenHandle < 0)
            {
                iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                strErr = "启动监听失败，错误号：" + iLastErr; //撤防失败，输出错误号
                Console.WriteLine(strErr);
            }
            else
            {
                Console.WriteLine("成功启动监听！");                
            }
        }
        private static void StopListenTcp()
        {
            if (!CHCNetSDK.NET_DVR_StopListen_V30(iListenHandle))
            {
                iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                strErr = "停止监听失败，错误号：" + iLastErr; //撤防失败，输出错误号
                Console.WriteLine(strErr);
            }
            else
            {
                Console.WriteLine("停止监听！");
                
            }
        }
        #endregion
        #region connection to website
        //connect to website
        public static void ConnectToHub()
        {
            try
            {
                con = new HubConnection("http://localhost:8080/");
                // con.TraceLevel = TraceLevels.All;
                // con.TraceWriter = Console.Out;
                proxy = con.CreateHubProxy("myHub");
                // MessageBox.Show("create proxy hub called");
                proxy.On<string,string>("AlarmEvent", (mac,data) =>
                {
                    try
                    {
                        GetDeviceDetails gt = new GetDeviceDetails();
                        var s =gt.GetDeviceDetailByMachine(mac);
                        if (data == "TriggerOn")
                        {
                            LoginDevices(s.DeviceIpT, s.Password, s.UserId, s.Port, s.ClassId,s.Ccmac);
                            //LoginDevices(s.DeviceIpSt, s.Password, s.UserId, s.Port, s.ClassId,s.Ccmac);
                        }
                        else if (data == "TriggerOff")
                        {
                            var d = deviceInfos.Where(x => x.Ccmac == mac.ToUpper()).Select(x => x).FirstOrDefault();
                            if (d != null)
                            {
                                CloseAlarm(d);
                            }
                        }
                    }
#pragma warning disable CS0168 // The variable 'ex' is declared but never used
                    catch (Exception ex)
#pragma warning restore CS0168 // The variable 'ex' is declared but never used
                    {
                        // Console.WriteLine(ex.Message);
                    }
                });
                con.Start().ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        Console.WriteLine("There was an error opening the connection with WebClient");
                    }
                    //else{MessageBox.Show("Connected to signalR");}
                }).Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine("not connected to WebClient " + ex.Message);
                con.StateChanged += Con_StateChanged;
            }
        }
        public static void SendMessageToWeb(AlarmInfo alm)
        {
            Dictionary<string, object> message1 = new Dictionary<string, object>();

            //message1.Add("test", "success");
            try
            {
                if (con.State != ConnectionState.Connected)
                {
                    // Console.WriteLine("connecting to server");
                    con.Start().Wait();
                    // Console.WriteLine("connected");
                }
                proxy.Invoke("AlarmMessage",Newtonsoft.Json.JsonConvert.SerializeObject(alm) );
                //Console.WriteLine("Sent to signalR server by" + sender);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                //  Console.WriteLine("connecting to server");
                con.Start().Wait();
                // Console.WriteLine("connected");
            }
        }
        private static async Task StartCon()
        {
            await con.Start();
        }
        private static void Con_StateChanged(StateChange obj)
        {
            if (obj.OldState == ConnectionState.Disconnected)
            {
                // Console.WriteLine("State changed inside");
                var current = DateTime.Now.TimeOfDay;
                SetTimer(current.Add(TimeSpan.FromSeconds(30)), TimeSpan.FromSeconds(10), StartCon);
                // Console.WriteLine("State changed inside done");
            }
            else
            {
                // Console.WriteLine("State changed else inside");
                if (_timer != null)
                    _timer.Dispose();
            }
        }

        private static void Con_Closed()
        {
            //Console.WriteLine("connection closed");
            con.Start().Wait();
        }

        private static void SetTimer(TimeSpan starTime, TimeSpan every, Func<Task> action)
        {
            var current = DateTime.Now;
            var timeToGo = starTime - current.TimeOfDay;
            if (timeToGo < TimeSpan.Zero)
            {
                return;
            }
            _timer = new Timer(x =>
            {
                action.Invoke();
            }, null, timeToGo, every);
        }
        #endregion
    }

    class AlarmInfo
    {
       public string AlarmTime { get; set; }
       public string DeviceIp { get; set; }
       public string AlarmMsg { get; set; }
    }
    class DeviceInfo
    {
        public int ClassId { get; set; }
        public int MuserId { get; set; }
        public string DeviceIp { get; set; }
        public string Message { get; set; }
        public string Ccmac { get; set; }
    }
}
