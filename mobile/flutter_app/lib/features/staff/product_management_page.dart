import 'package:flutter/material.dart';
import '../../core/api_client.dart';

class ProductManagementPage extends StatefulWidget {
  const ProductManagementPage({super.key, required this.api});
  final PosApiClient api;
  @override State<ProductManagementPage> createState() => _ProductManagementPageState();
}

class _ProductManagementPageState extends State<ProductManagementPage> {
  List<dynamic> products = [], categories = [];
  bool loading = true;
  String? error;

  @override void initState() { super.initState(); load(); }
  Future<void> load() async { try { final c=await widget.api.categories(); final p=await widget.api.get('/api/products'); if(mounted)setState((){categories=c;products=List<dynamic>.from(p);loading=false;}); } catch(e){if(mounted)setState((){error=e.toString();loading=false;});} }

  Future<void> edit([Map<String,dynamic>? existing]) async {
    final name=TextEditingController(text: existing?['name']?.toString() ?? '');
    final price=TextEditingController(text: existing?['price']?.toString() ?? '');
    final description=TextEditingController(text: existing?['description']?.toString() ?? '');
    int? category=existing?['categoryId'] as int? ?? (categories.isNotEmpty ? categories.first['id'] as int : null);
    bool available=existing?['available'] as bool? ?? true;
    final result=await showDialog<bool>(context:context,builder:(ctx)=>StatefulBuilder(builder:(ctx,setLocal)=>AlertDialog(title:Text(existing==null?'Add product':'Edit product'),content:SingleChildScrollView(child:Column(mainAxisSize:MainAxisSize.min,children:[TextField(controller:name,decoration:const InputDecoration(labelText:'Name')),TextField(controller:price,keyboardType:const TextInputType.numberWithOptions(decimal:true),decoration:const InputDecoration(labelText:'Price')),TextField(controller:description,decoration:const InputDecoration(labelText:'Description')),DropdownButtonFormField<int>(value:category,items:categories.map((c)=>DropdownMenuItem<int>(value:c['id'] as int,child:Text(c['name'].toString()))).toList(),onChanged:(v)=>setLocal(()=>category=v),decoration:const InputDecoration(labelText:'Category')),SwitchListTile(value:available,onChanged:(v)=>setLocal(()=>available=v),title:const Text('Available'))])),actions:[TextButton(onPressed:()=>Navigator.pop(ctx),child:const Text('Cancel')),FilledButton(onPressed:()=>Navigator.pop(ctx,true),child:const Text('Save'))])));
    if(result!=true||name.text.trim().isEmpty||category==null)return;
    final value=double.tryParse(price.text.trim()); if(value==null||value<0)return;
    try { if(existing==null){await widget.api.createProduct(categoryId:category!,name:name.text.trim(),price:value,description:description.text.trim(),available:available);}else{await widget.api.updateProduct(existing['id'] as int,categoryId:category!,name:name.text.trim(),price:value,description:description.text.trim(),available:available);} await load(); }catch(e){if(mounted)ScaffoldMessenger.of(context).showSnackBar(SnackBar(content:Text(e.toString())));}
  }

  Future<void> addCategory() async { final c=TextEditingController(); final ok=await showDialog<bool>(context:context,builder:(ctx)=>AlertDialog(title:const Text('Add category'),content:TextField(controller:c,decoration:const InputDecoration(labelText:'Name')),actions:[TextButton(onPressed:()=>Navigator.pop(ctx),child:const Text('Cancel')),FilledButton(onPressed:()=>Navigator.pop(ctx,true),child:const Text('Add'))])); if(ok==true&&c.text.trim().isNotEmpty){try{await widget.api.createCategory(c.text.trim());await load();}catch(e){if(mounted)ScaffoldMessenger.of(context).showSnackBar(SnackBar(content:Text(e.toString())));}}}

  @override Widget build(BuildContext context)=>Scaffold(appBar:AppBar(title:const Text('Products'),actions:[IconButton(onPressed:addCategory,icon:const Icon(Icons.category)),IconButton(onPressed:load,icon:const Icon(Icons.refresh))]),floatingActionButton:FloatingActionButton(onPressed:()=>edit(),child:const Icon(Icons.add)),body:loading?const Center(child:CircularProgressIndicator()):error!=null?Center(child:Text(error!)):ListView.builder(itemCount:products.length,itemBuilder:(_,i){final p=Map<String,dynamic>.from(products[i]);return ListTile(title:Text(p['name'].toString()),subtitle:Text('Rs. ${p['price']} • ${p['available']==true?'Available':'Unavailable'}'),trailing:IconButton(onPressed:()=>edit(p),icon:const Icon(Icons.edit)));}));
}
