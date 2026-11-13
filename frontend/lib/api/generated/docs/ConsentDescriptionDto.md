
# ConsentDescriptionDto


## Properties

Name | Type
------------ | -------------
`clientId` | string
`clientName` | string
`redirectTarget` | string
`redirectsToThisComputer` | boolean
`scope` | string
`resource` | string
`allows` | Array&lt;string&gt;
`neverAllows` | Array&lt;string&gt;

## Example

```typescript
import type { ConsentDescriptionDto } from ''

// TODO: Update the object below with actual values
const example = {
  "clientId": null,
  "clientName": null,
  "redirectTarget": null,
  "redirectsToThisComputer": null,
  "scope": null,
  "resource": null,
  "allows": null,
  "neverAllows": null,
} satisfies ConsentDescriptionDto

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as ConsentDescriptionDto
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


