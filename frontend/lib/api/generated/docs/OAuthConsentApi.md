# OAuthConsentApi

All URIs are relative to *http://localhost:5019*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**oAuthConsentDecide**](OAuthConsentApi.md#oauthconsentdecide) | **POST** /api/oauth/consent |  |
| [**oAuthConsentDescribe**](OAuthConsentApi.md#oauthconsentdescribe) | **POST** /api/oauth/consent/describe |  |
| [**oAuthConsentListGrants**](OAuthConsentApi.md#oauthconsentlistgrants) | **GET** /api/oauth/grants |  |
| [**oAuthConsentRevokeGrant**](OAuthConsentApi.md#oauthconsentrevokegrant) | **POST** /api/oauth/grants/{id}/revoke |  |



## oAuthConsentDecide

> ConsentResultDto oAuthConsentDecide(consentDecisionDto)



Requires a signed-in session: an API token is refused here.

### Example

```ts
import {
  Configuration,
  OAuthConsentApi,
} from '';
import type { OAuthConsentDecideRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const config = new Configuration({ 
    // Configure HTTP bearer authorization: Bearer
    accessToken: "YOUR BEARER TOKEN",
  });
  const api = new OAuthConsentApi(config);

  const body = {
    // ConsentDecisionDto
    consentDecisionDto: ...,
  } satisfies OAuthConsentDecideRequest;

  try {
    const data = await api.oAuthConsentDecide(body);
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
| **consentDecisionDto** | [ConsentDecisionDto](ConsentDecisionDto.md) |  | |

### Return type

[**ConsentResultDto**](ConsentResultDto.md)

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

- **Content-Type**: `application/json`
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** |  |  -  |
| **400** |  |  -  |
| **401** |  |  -  |
| **503** |  |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## oAuthConsentDescribe

> ConsentDescriptionDto oAuthConsentDescribe(authorizeRequestDto)



Requires a signed-in session: an API token is refused here.

### Example

```ts
import {
  Configuration,
  OAuthConsentApi,
} from '';
import type { OAuthConsentDescribeRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const config = new Configuration({ 
    // Configure HTTP bearer authorization: Bearer
    accessToken: "YOUR BEARER TOKEN",
  });
  const api = new OAuthConsentApi(config);

  const body = {
    // AuthorizeRequestDto
    authorizeRequestDto: ...,
  } satisfies OAuthConsentDescribeRequest;

  try {
    const data = await api.oAuthConsentDescribe(body);
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
| **authorizeRequestDto** | [AuthorizeRequestDto](AuthorizeRequestDto.md) |  | |

### Return type

[**ConsentDescriptionDto**](ConsentDescriptionDto.md)

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

- **Content-Type**: `application/json`
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** |  |  -  |
| **400** |  |  -  |
| **401** |  |  -  |
| **503** |  |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## oAuthConsentListGrants

> Array&lt;OAuthGrantDto&gt; oAuthConsentListGrants()



Requires a signed-in session: an API token is refused here.

### Example

```ts
import {
  Configuration,
  OAuthConsentApi,
} from '';
import type { OAuthConsentListGrantsRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const config = new Configuration({ 
    // Configure HTTP bearer authorization: Bearer
    accessToken: "YOUR BEARER TOKEN",
  });
  const api = new OAuthConsentApi(config);

  try {
    const data = await api.oAuthConsentListGrants();
    console.log(data);
  } catch (error) {
    console.error(error);
  }
}

// Run the test
example().catch(console.error);
```

### Parameters

This endpoint does not need any parameter.

### Return type

[**Array&lt;OAuthGrantDto&gt;**](OAuthGrantDto.md)

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** |  |  -  |
| **401** |  |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## oAuthConsentRevokeGrant

> OAuthGrantDto oAuthConsentRevokeGrant(id)



Requires a signed-in session: an API token is refused here.

### Example

```ts
import {
  Configuration,
  OAuthConsentApi,
} from '';
import type { OAuthConsentRevokeGrantRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const config = new Configuration({ 
    // Configure HTTP bearer authorization: Bearer
    accessToken: "YOUR BEARER TOKEN",
  });
  const api = new OAuthConsentApi(config);

  const body = {
    // number
    id: 56,
  } satisfies OAuthConsentRevokeGrantRequest;

  try {
    const data = await api.oAuthConsentRevokeGrant(body);
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
| **id** | `number` |  | [Defaults to `undefined`] |

### Return type

[**OAuthGrantDto**](OAuthGrantDto.md)

### Authorization

[Bearer](../README.md#Bearer)

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** |  |  -  |
| **401** |  |  -  |
| **404** |  |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

