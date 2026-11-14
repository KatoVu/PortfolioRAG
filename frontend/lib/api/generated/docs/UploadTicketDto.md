
# UploadTicketDto


## Properties

Name | Type
------------ | -------------
`uploadUrl` | string
`method` | string
`field` | string
`expiresAt` | Date
`maxImageBytes` | number
`maxVideoBytes` | number

## Example

```typescript
import type { UploadTicketDto } from ''

// TODO: Update the object below with actual values
const example = {
  "uploadUrl": null,
  "method": null,
  "field": null,
  "expiresAt": null,
  "maxImageBytes": null,
  "maxVideoBytes": null,
} satisfies UploadTicketDto

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as UploadTicketDto
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


