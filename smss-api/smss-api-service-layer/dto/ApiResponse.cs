using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.dto
{
    public class ApiResponse<T>
    {
        public bool Status { get; set; }

        public int StatusCode { get; set; }

        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }
}
