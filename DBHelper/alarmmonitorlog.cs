//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace DBHelper
{
    using System;
    using System.Collections.Generic;
    
    public partial class alarmmonitorlog
    {
        public int id { get; set; }
        public string almMessage { get; set; }
        public System.DateTime almTime { get; set; }
        public int Classid { get; set; }
        public string deviceip { get; set; }
    
        public virtual classdetail classdetail { get; set; }
    }
}
