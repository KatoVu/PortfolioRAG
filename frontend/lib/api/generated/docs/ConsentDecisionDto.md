
# ConsentDecisionDto


## Properties

Name | Type
------------ | -------------
`clientId` | string
`redirectUri` | string
`responseType` | string
`codeChallenge` | string
`codeChallengeMethod` | string
`state` | string
`scope` | string
`resource` | string
`approve` | boolean
`fullEditing` | boolean

## Example

```typescript
import type { ConsentDecisionDto } from ''

// TODO: Update the object below with actual values
const example = {
  "clientId": null,
  "redirectUri": null,
  "responseType": null,
  "codeChallenge": null,
  "codeChallengeMethod": null,
  "state": null,
  "scope": null,
  "resource": null,
  "approve": null,
  "fullEditing": null,
} satisfies ConsentDecisionDto

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as ConsentDecisionDto
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


