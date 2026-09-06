import 'dart:async';
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import '../../core/api_client.dart';

class RiderTrackingPage extends StatefulWidget {
  const RiderTrackingPage({super.key, required this.api});
  final PosApiClient api;
  @override State<RiderTrackingPage> createState() => _RiderTrackingPageState();
}

class _RiderTrackingPageState extends State<RiderTrackingPage> {
  Timer? timer; bool online = false; String status = 'Offline'; Position? position; String? error;
  @override void dispose(){timer?.cancel();super.dispose();}
  Future<void> toggle(bool value) async {
    if (value) { if (!await Geolocator.isLocationServiceEnabled()) { setState(()=>error='Enable device location first.'); return; } var permission=await Geolocator.checkPermission(); if(permission==LocationPermission.denied) permission=await Geolocator.requestPermission(); if(permission==LocationPermission.denied || permission==LocationPermission.deniedForever){setState(()=>error='Location permission is required.');return;} }
    await widget.api.riderAvailability(value); setState(()=>online=value); timer?.cancel(); if(value){ timer=Timer.periodic(const Duration(seconds:10),(_)=>sendLocation()); await sendLocation(); }
  }
  Future<void> sendLocation() async { try { final p=await Geolocator.getCurrentPosition(locationSettings: const LocationSettings(accuracy: LocationAccuracy.high)); position=p; await widget.api.riderLocation(p.latitude,p.longitude,accuracy:p.accuracy,speed:p.speed,heading:p.heading); if(mounted)setState(()=>status='GPS shared ${DateTime.now().toLocal()}'); } catch(e){if(mounted)setState(()=>error=e.toString());} }
  @override Widget build(BuildContext context)=>Scaffold(appBar:AppBar(title:const Text('Rider GPS')),body:Padding(padding:const EdgeInsets.all(16),child:Column(crossAxisAlignment:CrossAxisAlignment.start,children:[SwitchListTile(title:const Text('Available for live delivery'),subtitle:Text(status),value:online,onChanged:toggle),if(position!=null)Card(child:ListTile(title:Text('${position!.latitude.toStringAsFixed(6)}, ${position!.longitude.toStringAsFixed(6)}'),subtitle:Text('Accuracy ${position!.accuracy.toStringAsFixed(1)}m • Speed ${position!.speed.toStringAsFixed(1)} m/s'))),if(error!=null)Text(error!,style:TextStyle(color:Theme.of(context).colorScheme.error)),const SizedBox(height:12),FilledButton.icon(onPressed:online?sendLocation:null,icon:const Icon(Icons.my_location),label:const Text('Send Location Now'))])));
}