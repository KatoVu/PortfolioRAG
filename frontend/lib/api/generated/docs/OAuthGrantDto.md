
# OAuthGrantDto


## Properties

Name | Type
------------ | -------------
`id` | number
`clientName` | string
`scope` | string
`createdAt` | Date
`lastUsedAt` | Date
`revokedAt` | Date
`isActive` | boolean

## Example

```typescript
import type { OAuthGrantDto } from ''

// TODO: Update the object below with actual values
const example = {
  "id": null,
  "clientName": null,
  "scope": null,
  "createdAt": null,
  "lastUsedAt": null,
  "revokedAt": null,
  "isActive": null,
} satisfies OAuthGrantDto

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as OAuthGrantDto
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


