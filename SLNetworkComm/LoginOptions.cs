//  Copyright (c) 2008 - 2011, www.metabolt.net (METAbolt)
//  Copyright (c) 2006-2008, Paul Clement (a.k.a. Delta)
//  All rights reserved.

//  Redistribution and use in source and binary forms, with or without modification, 
//  are permitted provided that the following conditions are met:

//  * Redistributions of source code must retain the above copyright notice, 
//    this list of conditions and the following disclaimer. 
//  * Redistributions in binary form must reproduce the above copyright notice, 
//    this list of conditions and the following disclaimer in the documentation 
//    and/or other materials provided with the distribution. 

//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
//  ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
//  WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
//  IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, 
//  INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT 
//  NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR 
//  PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
//  WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
//  ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
//  POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenMetaverse;

namespace SLNetworkComm
{
    public class LoginOptions
    {
        private string firstName;
        private string lastName;
        private string password;
        private bool isPasswordMD5 = false;
        private string author = string.Empty;
        private string userAgent = string.Empty;

        private StartLocationType startLocation = StartLocationType.Home;
        private string startLocationCustom = string.Empty;

        private LoginGrid grid = LoginGrid.MainGrid;
        private string gridCustomLoginUri = string.Empty;
        private LastExecStatus crashstatus = LastExecStatus.Normal;

        public LoginOptions()
        {

        }

        public string FirstName
        {
            get { return firstName; }
            set { firstName = value; }
        }

        public string LastName
        {
            get { return lastName; }
            set { lastName = value; }
        }

        public string FullName
        {
            get
            {
                if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName))
                    return string.Empty;
                else
                    return firstName + " " + lastName;
            }
        }

        public string Password
        {
            get { return password; }
            set { password = value; }
        }

        public bool IsPasswordMD5
        {
            get { return isPasswordMD5; }
            set { isPasswordMD5 = value; }
        }

        public StartLocationType StartLocation
        {
            get { return startLocation; }
            set { startLocation = value; }
        }

        public string StartLocationCustom
        {
            get { return startLocationCustom; }
            set { startLocationCustom = value; }
        }

        public string UserAgent
        {
            get { return userAgent; }
            set { userAgent = value; }
        }

        public string Author
        {
            get { return author; }
            set { author = value; }
        }

        public LoginGrid Grid
        {
            get { return grid; }
            set { grid = value; }
        }

        public string GridCustomLoginUri
        {
            get { return gridCustomLoginUri; }
            set { gridCustomLoginUri = value; }
        }

        public LastExecStatus CrashStatus
        {
            get { return crashstatus; }
            set { crashstatus = value; }
        }
    }
}
