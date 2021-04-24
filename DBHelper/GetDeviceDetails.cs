// ***********************************************************************
// Assembly         : DBHelper
// Author           : admin
// Created          : 01-21-2021
//
// Last Modified By : admin
// Last Modified On : 02-01-2021
// ***********************************************************************
// <copyright file="GetDeviceDetails.cs" company="">
//     Copyright ©  2021
// </copyright>
// <summary></summary>
// ***********************************************************************
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBHelper
{
    /// <summary>
    /// Class GetDeviceDetails.
    /// </summary>
    public class GetDeviceDetails
    {
      
        /// <summary>
        /// Gets the camera detail by machine mac address.
        /// </summary>
        /// <param name="ccmac">The ccmac.</param>
        /// <returns>CamDetails.</returns>
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
        /// <summary>
        /// Saves the alarm messages
        /// </summary>
        /// <param name="ip">The ip.</param>
        /// <param name="msg">The MSG.</param>
        /// <param name="altime">The altime.</param>
        /// <returns>System.Int32.</returns>
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
        
    }

    /// <summary>
    /// Class CamDetails.
    /// </summary>
    public class CamDetails
    {
        /// <summary>
        /// Gets or sets the class identifier.
        /// </summary>
        /// <value>The class identifier.</value>
        public int ClassId { get; set; }
        /// <summary>
        /// Gets or sets the device ip t.
        /// </summary>
        /// <value>The device ip t.</value>
        public string DeviceIpT { get; set; }
        /// <summary>
        /// Gets or sets the user identifier.
        /// </summary>
        /// <value>The user identifier.</value>
        public string UserId { get; set; }
        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        /// <value>The password.</value>
        public string Password { get; set; }
        /// <summary>
        /// Gets or sets the port.
        /// </summary>
        /// <value>The port.</value>
        public int Port { get; set; }
        /// <summary>
        /// Gets or sets the device ip st.
        /// </summary>
        /// <value>The device ip st.</value>
        public string DeviceIpSt { get; set; }
        /// <summary>
        /// Gets or sets the ccmac.
        /// </summary>
        /// <value>The ccmac.</value>
        public string Ccmac { get; set; }
    }
}
