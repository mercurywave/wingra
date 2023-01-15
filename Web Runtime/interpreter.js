
// this is a bit of a hack so I can use enums as keys
// I can't use objects as keys, so I use constants that will be cached here
var __enumRefs = [];

class ORuntime {
	constructor() {
		this.StaticGlo = {};
		this.StaticFile = {};
		this.ScratchFile = {};
		this.AllFiles = {};
		this._initFuncs = [];
		this._initIdx = 0;
		this._doneInit = false;
		this._requiredSymbols = {};
		this._exportedSymbols = {};
		this._afterInitTasks = [];
		this._jobs = [];
		this._jobId = 1;
		this._tasks = [];
		this._taskId = 1;
		this._pipes = [];
		this._pipeId = 1;
		this.setupStandardHooks();
	}

	setupStandardHooks() {
		const _run = this;
		this.AddExternalFunction("IO.Log", function (val) { console.log(val); });
		this.AddExternalFunction("IO.DebugLog", function (val) { console.log(val); });
		this.AddExternalFunction("IO.Write", function (val) { console.log(val); });

		this.AddExternalMethod("Obj.NextKey", function (val) { return OObj.getNextKey(this, val); });
		this.AddExternalMethod("Obj.PrevKey", function (val) { return OObj.getPrevKey(this, val); });
		this.AddExternalMethod("Obj.Count", function () { return OObj.ChildCount(this) });
		this.AddExternalMethod("Obj.Keys", function () { return new OObj(null, OObj.getKeys(this)); });
		this.AddExternalMethod("Obj.HasChildren", function () { return OObj.ChildCount(this) > 0; });
		this.AddExternalMethod("Obj.ShallowCopy", function () { return new OObj(null, { ...this.inner }) });
		this.AddExternalMethod("Obj.Owns", function(key) { return OObj.Owns(this, key); });

		this.AddExternalMethod("Set.Has", function (val) { return OObj.HasChildKey(this, val); });

		this.AddExternalMethod("List.Add", function (val) {
			const len = OObj.ChildCount(this);
			var next;
			if (len == 0) next = 0;
			else next = +OObj.getLastKey(this) + 1;
			OObj.SetChild(this, next, val);
			return DU.Ref(val);
		});
		this.AddExternalMethod("List.Contains", function (val) {
			for (var k in this.inner) {
				if (gtInner(this.inner[k]) == gtInner(val))
					return true;
			}
			return false;
		});
		this.AddExternalMethod("List.Any", function (lamb) {
			for (var k in this.inner)
				if (OObj.RunLambda(lamb, { $it: this.inner[k] }))
					return true;
			return false;
		});
		this.AddExternalMethod("List.RemoveAll", function (lamb) {
			for (var k in this.inner)
				if (OObj.RunLambda(lamb, { $it: this.inner[k] }))
					delete this.inner[k];
		});


		this.AddExternalMethod("Stack.Push", function (val) {
			const len = OObj.ChildCount(this);
			OObj.SetChild(this, len, val);
			return DU.Ref(val);
		});
		this.AddExternalMethod("Stack.Pop", function () {
			const len = OObj.ChildCount(this);
			if (len == 0) { return null; }
			return OObj.FreePopChild(this, len - 1);
		});


		this.AddExternalMethod("Queue.Enqueue", function (val) {
			var last = OObj.getLastKey(this);
			if (last == null) { last = -1; }
			OObj.SetChild(this, last + 1, val);
		});
		this.AddExternalMethod("Queue.Dequeue", function () {
			var first = OObj.getFirstKey(this);
			return OObj.FreePopChild(this, first);
		});
		this.AddExternalFunction("Queue.New", function (val) { return new OObj(); });


		this.AddExternalMethod("Str.Replace", function (search, replace) { 
			return this.replace(new RegExp(escapeRegEx("" + search), 'g'), escapeRegEx("" + replace)); 
		});
		this.AddExternalMethod("Str.Piece", function (delim, piece) {
			const split = this.split(delim);
			if (piece < 1 || piece > split.length) { return ""; }
			if (piece === undefined) piece = 1;
			return split[piece - 1];
		});
		this.AddExternalMethod("Str.SubStr", function (start, len) {
			if (start < 0) { len += start; start = 0; }
			if (len == 0) { return ""; }
			if (len < 0) { trace( 'SubStr length must be positive'); }
			return this.substring(start, len);
		});
		this.AddExternalMethod("Str.Len", function () { return this.length; });
		this.AddExternalMethod("Str.Contains", function (search) { return this.includes(search); });
		this.AddExternalMethod("Str.Split", function (delim) {
			var split = this.split(delim);
			return new OObj(null, split);
		});
		this.AddExternalMethod("Str.ToUpper", function () { return this.toUpperCase(); });
		this.AddExternalMethod("Str.ToLower", function () { return this.toLowerCase(); });
		this.AddExternalMethod("Str.Trim", function () { return this.trim(); });

		this.AddExternalFunction("Scratch.Alloc", () => new OObj(_run));
		this.AddExternalFunction("Scratch.Free", (node) => {
			if (node.parent != _run) { return; }
			for (var key in node.inner) {
				if (node.inner.hasOwnProperty(key)) { delete node.inner[key]; }
			}
			delete node.inner;
		});
		this.AddExternalFunction("Scratch.Hoist", (obj) => obj);

		this.AddExternalFunctionAsync("Job.Yield", async () => await DU.Yield());
		this.AddExternalFunctionAsync("Job.Pause", async (dur) => { await new Promise(res => setTimeout(res, dur)); });
		this.AddExternalMethodAsync("Job.Wait", async function () {
			if (this in _run._jobs) await _run._jobs[this];
		});
		this.AddExternalMethod("Job.IsComplete", function (id) {
			return !(this in _run._jobs);
		});


		this.AddExternalFunction("Promise.Create", () => {
			var id = this._taskId++;
			_run._tasks[id] = MakeDeferral(id);
			return id;
		});
		this.AddExternalMethod("Promise.Resolve", function () {
			var job = _run._tasks[this];
			job.Complete();
			delete _run._tasks[this];
		});
		this.AddExternalMethodAsync("Promise.Wait", async function () {
			var job = _run._tasks[this];
			await job;
		});

		this.AddExternalFunction("Pipe.Create", () => {
			var id = this._pipeId++;
			_run._pipes[id] = MakeDeferral(id);
			return id;
		});
		this.AddExternalMethod("Pipe.Kill", function () {
			if(_run._pipes[this] == null) return;
			var job = _run._pipes[this];
			job.KILLED = true;
			job.Complete();
			delete _run._pipes[this];
		});
		this.AddExternalMethod("Pipe.IsLive", function () {
			return _run._pipes[this] && !_run._pipes[this].KILLED;
		});
		this.AddExternalMethod("Pipe.Write", function (data) {
			var job = _run._pipes[this];
			if(!job || job.KILLED) throw "pipe is already closed";
			job.data = data;
			job.Complete();
			delete _run._pipes[this];
		});
		this.AddExternalMethod("Pipe.Clear", function () {
			var job = _run._pipes[this];
			if(!job || job.KILLED) return;
			job.data = null;
		});
		this.AddExternalMethod("Pipe.HasData", function () {
			var job = _run._pipes[this];
			if(!job || job.KILLED) return false;
			return job.data != null;
		});
		this.AddExternalMethodAsync("Pipe.ReadAsync", async function () {
			var job = _run._pipes[this];
			if(!job || job.KILLED) return null;
			await job;
			return job.data;
		});
		this.AddExternalMultiMethod("Pipe.TryRead", function () {
			var job = _run._pipes[this];
			if(!job || job.KILLED) return [null , false];
			return [job.data, true];
		});


		this.AddExternalFunction("Math.Mod", (val, div) => {
			val = gVal(val);
			div = gVal(div);
			return val < 0 ? div - ((-val) % div) : val % div;
		});
		this.AddExternalFunction("Math.Div", (val, div) => {
			val = gVal(val);
			div = gVal(div);
			return Math.trunc(val < 0 ? (val - div) / div : val / div);
		});
		this.AddExternalFunction("Math.Floor", val => {
			val = gVal(val);
			return Math.floor(val);
		});
		this.AddExternalFunction("Math.Ceiling", val => {
			val = gVal(val);
			return Math.ceil(val);
		});
		this.AddExternalFunction("Math.Round", val => {
			val = gVal(val); return Math.round(val);
		});
		this.AddExternalFunction("Math.RoundToNearest", (val, nearest) => {
			val = gVal(val); nearest = gVal(nearest);
			return Math.round(val / nearest) * nearest;
		});
		this.AddExternalFunction("Math.Sqrt", val => {
			val = gVal(val);
			return Math.sqrt(val);
		});
		this.AddExternalFunction("Math.Atan2", (y, x) => {
			x = gVal(x);
			y = gVal(y);
			return Math.atan2(y, x);
		});

		this.AddExternalFunction("Debug.Break", function () { debugger; });
		this.AddExternalFunction("Debug.ObjDebug", function (val) { return "" + val; });
		this.AddExternalFunction("Debug.TypeName", function (val) { return typeof val; });
	}

	InitByteCode(booter) {
		booter(this);
		for(this._initIdx = 0; this._initIdx < this._initFuncs.length - 1; ){
			this.runNextInitFunc();
		}
		this._doneInit = true;
		var main = this.tryGetStaticGlo("Main");
		if(main) {
			this.RunLambda(main);
		}
		var testMain = this.tryGetStaticGlo("TestMain");
		if(testMain) {
			this.RunLambda(testMain);
		}
	}
	runNextInitFunc() {
		var fn = this._initFuncs[this._initIdx++];
		if (this._initIdx >= this._initFuncs.length)
			throw 'encountered cyclical initialization loop';
		fn(null);
	}

	AddExternalFunction(path, lamb) {
		this.setStaticGlo(path, OObj.MakeFunc(function (_, ...args) { return [lamb.apply(null, args)]; }), true);
	}
	AddExternalMethod(path, lamb) {
		this.setStaticGlo(path, OObj.MakeFunc(function (scope, ...args) {
			return [lamb.apply(scope.__THIS, args)];
		}), true);
	}

	AddExternalMultiMethod(path, lamb) // ::Foo(=> x,y) // need to return array
	{
		this.setStaticGlo(path, OObj.MakeFunc(function (scope, ...args) {
			return lamb.apply(scope.__THIS, args);
		}), true);
	}

	AddExternalFunctionAsync(path, lamb) {
		this.setStaticGlo(path, OObj.MakeFunc(async function (_, ...args) {
			return [await lamb.apply(null, args)];
		}), true);
	}
	AddExternalMethodAsync(path, lamb) {
		this.setStaticGlo(path, OObj.MakeFunc(async function (scope, ...args) {
			return [await lamb.apply(scope.__THIS, args)];
		}), true);
	}
	AddExternalMethodAsyncMulti(path, lamb) {
		this.setStaticGlo(path, OObj.MakeFunc(async function (scope, ...args) {
			return await lamb.apply(scope.__THIS, args);
		}), true);
	}

	setStaticGlo(path, value, canOverwrite) {
		if(!canOverwrite && this.StaticGlo[path] != null) return;
		if (value instanceof OObj)
			value.parent = this; // make sure this doesn't get stolen
		this.StaticGlo[path] = value;
	}
	setStaticFile(file, path, value) {
		if (value instanceof OObj)
			value.parent = this;
		if (!this.StaticFile[file]) { this.StaticFile[file] = {}; }
		this.StaticFile[file][path] = value;
	}
	__prepEnum(path, value) {
		if (!(value instanceof OObj))
			value = new OObj(this, value);
		else
			value.parent = this;
		value.enum = path;
		__enumRefs[makeKey(value)] = value;
		return value;
	}
	setStaticGloEnum(path, value) {
		value = this.__prepEnum(path, value);
		this.setStaticGlo(path, value);
		var pair = this.getEnumParentPath(path);
		if(!this.StaticGlo[pair[0]]){
			this.StaticGlo[pair[0]] = new OObj(this);
		}
		OObj.SetChild(this.StaticGlo[pair[0]], pair[1], value);
	}
	setStaticFileEnum(file, path, value) {
		this.setStaticFile(file, path, this.__prepEnum(path, value));
		var pair = this.getEnumParentPath(path);
		if(!this.StaticFile[file][pair[0]]){
			this.StaticFile[file][pair[0]] = new OObj(this);
		}
		OObj.SetChild(this.StaticFile[file][pair[0]], pair[1], value);
	}
	getEnumParentPath(path){
		var arr = path.split('.');
		var out = arr.splice(-1, 1);
		return [arr.join('.'), out];
	}

	getStaticGlo(path) {
		var out = this.tryGetStaticGlo(path);
		if (out == undefined)
			throw 'dependency not loaded? ' + path;
		return out;
	}
	tryGetStaticGlo(path) {
		if (this.StaticGlo[path] != undefined)
			return DU.Ref(this.StaticGlo[path]);
		if(!this._doneInit){
			while(this.StaticGlo[path] == undefined){
				this.runNextInitFunc();
				if (this.StaticGlo[path] != undefined)
					return DU.Ref(this.StaticGlo[path]);
			}
		}
		return undefined;
	}
	getStaticFile(file, path) {
		return this.StaticFile[file][path];
	}

	setScratchFile(file, path, value) {
		if (!this.ScratchFile[file]) { this.ScratchFile[file] = {}; }
		this.ScratchFile[file][path] = value;
	}
	getScratchFile(file, path) {
		return this.ScratchFile[file][path];
	}

	runJob(tag) {
		const id = this._jobId++;
		var job = this.runJobAsync(tag);
		this._jobs[id] = job;
		job.then(() => delete this._jobs[id]);
		return id;
	}
	runJobLambda(obj) {
		if (!(obj instanceof OObj)) { trace("variable is not obj"); }
		if (!('func' in obj)) { trace( "variable is not lambda-like"); }
		var cap = { ...obj.inner };
		return this.runJob(async () => await obj.func({ capture: cap }));
	}
	async runJobAsync(tag) {
		await DU.Yield();
		await tag();
	}

	// public helper functions
	RunLambda(lambda, par) {
		// par is a key:value pairs of variables to inject
		return OObj.RunLambda(lambda, par);
	}
	CallLambdaTag(lambda, args, thisVar) {
		// args is array variables to pass in
		if (!(lambda instanceof OObj)) { trace( "variable is not obj"); }
		if (!('func' in lambda)) { trace( "variable is not lambda-like"); }
		return lambda.func.apply(null, [{ __THIS: thisVar }, ...args]);
	}
	RunTask(tag, ...params) {
		const fn = this.getStaticGlo(tag);
		this.CallLambdaTag(fn, params);
	}
}

function MakeDeferral(id) {
	var _resolve;
	var promise = new Promise(res => _resolve = res);
	promise.id = id;
	promise.Complete = _resolve;
	return promise;
}

class DU {
	static Ref(value) {
		if (value instanceof OObj) {
			return value.MakeRef();
		}
		return value;
	}
	static Copy(value) {
		if (value instanceof OObj) { return value.Copy(); }
		return value;
	}
	static MakeIter(obj) {
		if ('next' in obj) // this is a bad check, but apparently we don't have a way to tell if a function is an iterator
		{
			return OObj.MakeIterator(obj);
		}
		if (!(obj instanceof OObj)) { trace( "can't iterate on non-object"); }
		if (obj.iter) { return obj; }
		return OObj.MakeIterator(_makeIter(obj));
	}
	static ReadReturn(ret, count) {
		// expecting array of variables or iterator
		if (Array.isArray(ret)) {
			if (count <= 1) { return ret[0]; }
			return ret.slice(0, count).reverse()
		}
		if (ret == null) { return []; } // function with no return?
		if ('next' in ret) { return ret; } // iterator (hacky check)
		if ('then' in ret) { return []; } // promise - returned during arun (bad hack)
		trace( "can't handle return " + ret);
	}
	static async Yield() { await new Promise(res => setTimeout(res, 0)); }

	static AssertOwned(obj) {
		if ((obj instanceof OObj) && !obj.owned) {
			trace( 'parameter was not passed owned instance variable');
		}
	}

	static AreEqual(a, b) {
		if (a instanceof OObj && b instanceof OObj) {
			return a.inner == b.inner;
		}
		return gVal(a) == gVal(b);
	}

	static Divide(a, b) {
		if (Number.isInteger(a) && Number.isInteger(b))
			return Math.trunc(a / b);
		return a / b;
	}

	static FreeObj(obj) {
		if ((obj instanceof OObj) && !obj.owned) {
			obj.owned = false;
			obj.parent = null;
		}
	}
}
function* _makeIter(obj) // can't be static?
{
	var k = OObj.getFirstKey(obj);
	while (k != null) {
		yield [obj.inner[k]];
		k = OObj.getNextKey(obj, k);
	}
}

// wrapper to handle 
function gVal(val) {
	if (val && val.hasOwnProperty("enum"))
		return val.inner;
	return val;
}

function makeKey(key, parent) {
	if (key && key.hasOwnProperty("enum"))
		key = " " + String.fromCharCode(0) + key.enum;
	else if (key instanceof OObj){
		const obj = key;
		key = "_" + String.fromCharCode(0) + key.GetUniqueId();
		parent.keyMap[key] = obj;
	}
	return key;
}

var __uniqID = 0;
class OObj {
	constructor(parent, inner) {
		this.parent = parent ?? null;
		this.inner = inner ?? {};
		this.dirty = true;
		this.owned = true;
		this.keyMap = [];
		this.uniqId = __uniqID++;
	}

	static MakeFunc(func, scope) {
		var obj = new OObj(null, scope);
		obj.func = func;
		return obj;
	}
	static MakeIterator(iter) {
		var obj = new OObj(null);
		obj.iter = { iter: iter };
		obj.Next();
		return obj;
	}

	Copy(newParent) {
		var obj = new OObj(newParent);
		var dup = { ... this.inner };
		for (var key in dup) {
			if (dup[key] instanceof OObj) {
				if (dup[key].parent != null && dup[key].parent.inner === this.inner) {
					dup[key] = dup[key].Copy(obj);
				}
			}
		}
		obj.inner = dup;
		if ('func' in this) obj.func = this.func;
		obj.keyMap = [...this.keyMap];
		return obj;
	}
	
	static Owns(obj, key) {
		if (!(obj instanceof OObj)) { return false; }
		key = makeKey(key, obj);
		return (key in obj.inner) && (obj.inner[key] instanceof OObj) && (obj.inner[key].parent.inner === obj.inner);
	}

	MakeRef() {
		var obj = new OObj(this.parent, this.inner);
		obj.iter = this.iter;
		obj.func = this.func;
		obj.owned = false;
		obj.keyMap = this.keyMap;
		obj.uniqId = this.uniqId;
		if (this.hasOwnProperty("enum"))
			obj.enum = this.enum;
		return obj;
	}

	GetUniqueId(){
		return this.uniqId;
	}

	Next() {
		var iter = this.iter;
		const obj = iter.iter.next();
		iter.value = obj.value;
		iter.done = obj.done;
	}

	static GetPath(obj, pathArr) {
		for (var key of pathArr) {
			var node = makeKey(key, obj);
			if (obj instanceof OObj)
				obj = obj.inner[node];
			else if (obj == null)
				return null;
			else if (node in obj)
				obj = obj[node];
			else return null;
		}
		return obj;
	}

	static SetPath(obj, pathArr, value) {
		if (!(obj instanceof OObj)) { trace( "can't set path " + pathArr); }
		const leading = pathArr.slice(0, -1);
		const key = makeKey(pathArr[pathArr.length - 1], obj);
		for (var node of leading) {
			node = makeKey(node, obj);
			if (!obj instanceof OObj) { trace( "can't set path " + pathArr); }
			if (!(node in obj.inner)) { obj.inner[node] = new OObj(obj); }
			obj = obj.inner[node];
		}
		OObj.SetChild(obj, key, value);
	}

	static FreePath(obj, pathArr) {
		if (!obj instanceof OObj) { trace( "can't free, object is null"); }
		const leading = pathArr.slice(0, -1);
		const key = makeKey(pathArr[pathArr.length - 1], obj);
		for (var node of leading) {
			node = makeKey(node, obj);
			if (!obj instanceof OObj || !(node in obj.inner)) { trace( "can't free path " + pathArr); }
			obj = obj.inner[node];
		}
		return OObj.FreePopChild(obj, key);
	}

	static SetChild(obj, key, value) {
		if (!(obj instanceof OObj)) {
			// I'm not sure this is a good idea, but if I have an external object,
			// my life gets a lot easier if I allow chaining
			if (obj && typeof (obj) == "object") { obj[key] = value; }
			else trace( "can't set key " + key);
			return;
		}
		key = makeKey(key, obj);
		if (!(key in obj.inner)) { obj.dirty = true; }
		obj.inner[key] = value;
		if ((value instanceof OObj) && value.parent == null) {
			value.parent = obj;
			value.owned = true;
		}
	}
	static FreePopChild(obj, key) {
		key = makeKey(key, obj);
		if (!(obj instanceof OObj)) { return null; }
		if (!(key in obj.inner)) { return null; }
		if(obj.hasOwnProperty("keyMap"))
			delete obj.keyMap[key];
		obj.dirty = true;
		var pop = obj.inner[key];
		if(Array.isArray(obj.inner) && obj.inner.length && key == obj.inner.length - 1) {
			// special cast - arrays will leave empty elements, which is annoying at the end of the array
			obj.inner.pop();
		}
		else {
			delete obj.inner[key];
		}
		if ((pop instanceof OObj) && (pop.parent.uniqId == obj.uniqId)) {
			pop.parent = null;
			pop.owned = false;
		}
		return pop;
	}
	static HasChildKey(obj, key) // child is string
	{
		if (!(obj instanceof OObj)) { return false; }
		key = makeKey(key, obj);
		return key in obj.inner;
	}
	static DotAccess(obj, key) {
		if (!(obj instanceof OObj)) {
			// I'm not sure this is a good idea, but if I have an external object,
			// my life gets a lot easier if I allow chaining
			if (obj && typeof (obj) == "object") { return obj[key]; }
			console.trace();
			trace( "variable is not obj for .");
		}
		key = makeKey(key, obj);
		if (!(key in (obj.inner))) { trace( "object does not have key '" + key + "'"); }
		return obj.inner[key];
	}

	static getKeys(obj) {
		var inner = gtInner(obj);
		if (Array.isArray(inner)) {
			// this is finicky because empty array elements are a special case
			var list = [];
			for (const [index, element] of inner.entries()) {
				if(element != undefined) {
					list.push(index);
				}
			}
			return list;
		}
		return Object.keys(inner);
	}

	static getFirstKey(obj) {
		if (!(obj instanceof OObj)) { trace( "variable is not obj"); }
		obj._checkDirty();
		const keys = OObj.getKeys(obj);
		if (keys.length == 0) { return null; }
		return OObj._exportKey(keys[0], obj);
	}

	static getLastKey(obj) {
		if (!(obj instanceof OObj)) { trace( "variable is not obj"); }
		obj._checkDirty();
		const keys = OObj.getKeys(obj);
		if (keys.length == 0) { return null; }
		const k = keys[keys.length - 1];
		return OObj._exportKey(k, obj);
	}

	static getNextKey(obj, prev) {
		if (!(obj instanceof OObj)) { trace( "variable is not obj"); }
		obj._checkDirty();
		const keys = OObj.getKeys(obj);
		if (prev === undefined) prev = "";
		var idx = OObj._binarySearch(keys, "" + makeKey(prev, obj));
		if (idx >= 0) { return OObj._exportKey(keys[idx + 1], obj); }
		if (-idx > keys.length) { return null; }
		return OObj._exportKey(keys[-idx - 1], obj);
	}
	static getPrevKey(obj, prev) {
		if (!(obj instanceof OObj)) { trace( "variable is not obj"); }
		obj._checkDirty();
		const keys = OObj.getKeys(obj);
		if (prev === undefined || prev === "") { return OObj.getLastKey(obj); }
		var idx = OObj._binarySearch(keys, "" + makeKey(prev, obj));
		if (idx > 0) { return OObj._exportKey(keys[idx - 1], obj); }
		if (idx >= -1) { return null; }
		if (-idx - 2 > keys.length) { return OObj.getLastKey(obj); }
		return OObj._exportKey(keys[-idx - 2], obj);
	}
	static _exportKey(k, parent) {
		if (k && k.length > 2 && k.charCodeAt(1) === 0 && k.charAt(0) === " ")
			return __enumRefs[k];
		if (k && k.length > 2 && k.charCodeAt(1) === 0 && k.charAt(0) === "_")
			return parent.keyMap[k];
		return this._convKey(k);
	}
	static _convKey(k) {
		// keys collection doesn't retain the data type
		// barely matters, but I had a test that failed
		const num = Number(k);
		if (num === 0 && k !== "0") { return k; } // there are some dumb edge cases here
		if (!isNaN(num)) { return num; }
		return k;
	}

	static ChildCount(obj) {
		if (!(obj instanceof OObj)) { return 0; }
		if (Array.isArray(obj.inner)) { return obj.inner.length; }
		return Object.keys(obj.inner).length;
	}

	InjectCapture(key, obj){
		this.inner[key] = obj;
	}

	static RunLambda(obj, scopeVar) {
		return OObj._RunLambda(obj, scopeVar)[0];
	}
	static _RunLambda(obj, scopeVar) {
		if (!(obj instanceof OObj)) { trace( "variable is not obj"); }
		if (!('func' in obj)) { trace( "variable is not lambda-like"); }
		var cap = { ...obj.inner, ...scopeVar };
		return obj.func({ capture: cap });
	}

	_checkDirty() {
		if (!this.dirty) { return; }
		this._sortKeys();
		this.dirty = false;
	}
	_sortKeys() {
		// as long as I'm using the binary search for keys, this needs to run :(

		// we need to be careful and retain the object so pointers continue to work
		// numeric indexes are guaranteed sorted on modern browsers
		var keys = Object.keys(this.inner).filter(k => k != +k);
		keys.sort();
		var copy = [];
		for (let k of keys) {
			copy[k] = this.inner[k];
			delete this.inner[k];
		}
		for (let k of keys) {
			this.inner[k] = copy[k];
		}
	}

	static _binarySearch(array, value) {
		if (array.length == 0 || value === "") { return -1; }
		let high = array.length - 1;
		let low = 0;

		value = OObj._convKey(value);

		if (value < OObj._convKey(array[low]))
			return -1;
		if (value > OObj._convKey(array[high]))
			return -(high + 2);
		var mid = -1;
		while (high >= low) {
			mid = (high + low) >> 1;

			if (value === OObj._convKey(array[mid]))
				return mid;
			else if (value < OObj._convKey(array[mid]))
				high = mid - 1;
			else
				low = mid + 1;
		}
		mid = (high + low) >> 1;
		return -(mid + 2);
		// if <0, (-retVal-1) is where you insert
	}
}

class DHelp {
	static ObjContentToArray(obj) {
		var arr = [];
		if (!(obj instanceof OObj)) { return arr; }
		var keys = Object.keys(obj.inner);
		for (let k of keys) {
			arr.push(obj.inner[k]);
		}
		return arr;
	}
}

function mkObj(inner) {return new OObj(RT, inner);}
function gtInner(obj) {return (obj instanceof OObj) ? obj.inner : obj; }

function log(...p) { console.log(...p); }
function fatalError() { trace("Fatal Error!"); }
function trace(message) { 
	if (message) log(message);
	console.trace(); 
	throw null; 
}

function escapeRegEx(string) {
    return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}