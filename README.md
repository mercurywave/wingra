<img src="images/wingra.png" alt="Logo" width="250" align="right"/>

# Wingra
Wingra is a high-level procedural programming language which emphasizes static structural code flow.

## Primary Features
- Dynamic typing
- Block-based significant indentation
- Memory efficiency through ownership passing
- Statically resolvable references
- Flexible data composition
- Run interpreted or transpile to javascript
- Strong tooling

## Example Syntax
```ts
::CustomersWhoBought(product, ?atleast => customers)
    atleast ?: 1 // default to 1 if not passed
    using Set
    @set : $New()
    // find all completed orders
    for @order of ^AllOrders.$List.Where(`it.complete`)
        // check quantity
        if order.items[product] ? 0 >= atleast
            // mark customer as having order
            set.$Add(order.customer)
    // flatten to list
    customers : set.$ToList()
```
### Syntax Features
- Succint lambda expression syntax with \` \`
- Multiple named output parameters from functions
- Structural safety with async functions and error handling

## Why Use Wingra?

#### Get the benefits of dynamic typing without the common downsides
Static function resolution means language tooling can provide good auto-completion, signatures, and definitions.

<img src="images/definition.png" alt="Example showing function definition in VSCode" width="221px" />

This static code flow also means the compiler can automatically inline functions and constants for improved performance. This can open up many additional possibilities around code flow analysis in future.

The ownership passing model of memory management can avoid the performance penalty of garbage collection and reference counting.


## Quick Start
The quickest way to get started is to download the VSCode extension.
1. Download [VSCode](https://code.visualstudio.com/) for windows
2. Download the latest release .zip
3. Extract the wingralang folder to %USERPROFILE%\\.vscode\\extensions
4. Open VSCode to a new folder
5. Add a new file called 'test.wng'
6. Add the following code:
    ```ts
    $IO.Write("Greetings from Lake Wingra!")
    ```
8. Press ctrl+shift+p and select 'Wingra: Run current folder'

## Learning
The [Scripts/Tutorial](Scripts/Tutorials) folder contains a number of guides to basic syntax.

The [Scripts/Samples](Scripts/Samples) folder contains some more realistic samples of how the language can be used.

## Current State and Future
The language is still relatively young, and probably not suitable for widespread production use at this time. The syntax may yet change and some features may be radically changed or removed in addition to new features and platforms. Some errors are less than helpful, and may require investigation into the language code itself.

I would welcome feedback and suggestions through GitHub or Twitter.

#### Roadmap
- [ ] Additional extension libraries
- [ ] Improved compiler errors
- [ ] Interpreter performance improvements
- [ ] C# transpiler
- [ ] Rewrite parser for better AST
- [ ] (potentially) gradual typing?

### Where does the name come from?
Wingra is named for [Lake Wingra](https://en.wikipedia.org/wiki/Lake_Wingra), which itself takes its name from the word for "duck" in the language of the Ho-Chunk Nation.
