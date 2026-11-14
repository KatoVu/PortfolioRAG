# UploadsApi

All URIs are relative to *http://localhost:5019*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**uploadsUpload**](UploadsApi.md#uploadsupload) | **POST** /api/uploads/{ticket} |  |



## uploadsUpload

> MediaDto uploadsUpload(ticket, file)



### Example

```ts
import {
  Configuration,
  UploadsApi,
} from '';
import type { UploadsUploadRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new UploadsApi();

  const body = {
    // string
    ticket: ticket_example,
    // Blob (optional)
    file: BINARY_DATA_HERE,
  } satisfies UploadsUploadRequest;

  try {
    const data = await api.uploadsUpload(body);
    console.log(data);
  } catch (error) {
    console.error(error);
  }
}

// Run the test
example().catch(console.error);
```

### Parameters


| Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **ticket** | `string` |  | [Defaults to `undefined`] |
| **file** | `Blob` |  | [Optional] [Defaults to `undefined`] |

### Return type

[**MediaDto**](MediaDto.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: `multipart/form-data`
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **201** |  |  -  |
| **400** |  |  -  |
| **403** |  |  -  |
| **404** |  |  -  |
| **409** |  |  -  |
| **413** |  |  -  |
| **415** |  |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

