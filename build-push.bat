@docker build . -f samples\SampleApp.KubernetesApi\Dockerfile -t localhost:5000/sampleapp:%1 
@docker push localhost:5000/sampleapp:%1  
@kubectl apply -f .\samples\SampleApp.KubernetesApi\sampleapp.yml   
@kubectl get pods