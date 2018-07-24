﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Magicodes.Admin.Application.App.Common.Dto
{
    /// <summary>
    /// 获取枚举值
    /// </summary>
    public class GetEnumValuesListDto
    {
        /// <summary>
        /// 显示名（会进行语言处理）
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 值
        /// </summary>
        public int Value { get; set; }
    }
}