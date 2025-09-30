namespace SistemadeVentasSupermercado.Web.Core
{
   
       public class Response<T>
        {
            public bool IsSuccess { get; set; }     // Indica si la operación fue exitosa
            public string? Message { get; set; }     // Mensaje adicional (ej: "Producto creado correctamente")
            public List<string> Errors { get; set; } = new List<string>(); // Lista de errores si falla
            public T? Result { get; set; }           // El resultado de la operación (puede ser cualquier tipo)




            public static Response<T> Failure(Exception ex, string message = "ha ocurrido al generar la solicitud")
             {

            return new Response<T>
            {
                IsSuccess = false,
                Message = message,
                Errors = new List<String>
                        {
                           ex.Message,
                        }
            };
            }


        public static Response<T> Failure( string message, List<String> errors = null)
        {

            return new Response<T>
            {
                IsSuccess = false,
                Message = message,
                Errors = errors
            };
        }
        public static Response<T> Success(T result, String message = "Tarea realizada con exito")
        {

            return new Response<T>
            {
                IsSuccess = true,
                Message = message,
                Result = result
            };
        }

        public static Response<T> Success( String message = "Tarea realizada con exito")
        {

            return new Response<T>
            {
                IsSuccess = true,
                Message = message,
                
            };
        }

    }



   }
