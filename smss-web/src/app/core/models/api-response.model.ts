/** Every backend endpoint returns this envelope. */
export interface ApiResponse<T> {
  status: boolean;
  statusCode: number;
  message: string;
  data: T | null;
}
