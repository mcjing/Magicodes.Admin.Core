﻿using System;
using Abp.AutoMapper;
using Abp.Domain.Entities.Auditing;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Magicodes.Admin.App.User.Dto
{
    /// <summary>
    /// 登陆 输出参数
    /// </summary>
    public class AppLoginOutput    
    {
        /// <summary>
        /// 用户Id
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 手机号码
        /// </summary>
        public string Phone { get; set; }

        /// <summary>
        /// 访问AccessToken
        /// </summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// 过期时间
        /// </summary>
        public int ExpireInSeconds { get; set; }


    }
}