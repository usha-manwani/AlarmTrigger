using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBHelper
{
    public class GetDeviceDetails
    {
        //public List<CamDetails> GetDeviceDetail(){
        //    var result = new List<CamDetails>();
        //    using (var context = new organisationdatabaseEntities())
        //    {
        //        result = context.classdetails.Select(x => new CamDetails
        //        {
        //            ClassId = x.classID,
        //            DeviceIpSt = x.camipS.Trim(),
        //            DeviceIpT= x.camipT.Trim(),
        //            Password= x.campass.Trim(),
        //            UserId= x.camuserid.Trim(),
        //            Port= x.camport
        //        }).Take(1).ToList();
        //    }
        //    return result;
        //}
        public CamDetails GetDeviceDetailByMachine(string ccmac)
        {
            var result = new CamDetails();
            using (var context = new organisationdatabaseEntities())
            {
                result = context.classdetails.Where(x => x.ccmac == ccmac).Select(x => new CamDetails
                {
                    ClassId = x.classID,
                    DeviceIpSt = x.camipS.Trim(),
                    DeviceIpT = x.camipT.Trim(),
                    Password = x.campass.Trim(),
                    UserId = x.camuserid.Trim(),
                    Port = x.camport,
                    Ccmac = ccmac.ToUpper()
                }).FirstOrDefault();
            }
            return result;
        }
        public int SaveMonitoringDetails(string ip,string msg, string altime)
        {
            int result = 0;
            try
            {
                using (var context = new organisationdatabaseEntities())
                {
                    var cid = context.classdetails.Where(x => x.camipS == ip || x.camipT == ip).Select(x => x.classID).FirstOrDefault();
                    var monitorobj = new alarmmonitorlog()
                    {
                        almMessage = msg,
                        almTime = Convert.ToDateTime(altime),
                        Classid = cid,
                        deviceip = ip
                    };
                    context.alarmmonitorlogs.Add(monitorobj);
                    result=context.SaveChanges();
                }
            }
           catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return result;
        }
        //public int SaveCamLoginInfo(string ip, int classid)
        //{
        //    int result = 1;
        //    try
        //    {
        //        using (var context = new organisationdatabaseEntities())
        //        {
        //            var cid = context.classdetails.Where(x => x.camipS == ip || x.camipT == ip).Select(x => x.classID).FirstOrDefault();
        //            var monitorobj = new alarmmonitorlog()
        //            {
        //                almMessage = msg,
        //                almTime = Convert.ToDateTime(altime),
        //                Classid = cid,
        //                deviceip = ip
        //            };
        //            context.alarmmonitorlogs.Add(monitorobj);
        //            result = context.SaveChanges();
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine(ex.Message);
        //    }
        //    return result;
        //}
    }

    public class CamDetails
    {
        public int ClassId { get; set; }
        public string DeviceIpT { get; set; }
        public string UserId { get; set; }
        public string Password { get; set; }
        public int Port { get; set; }
        public string DeviceIpSt { get; set; }
        public string Ccmac { get; set; }
    }
}
